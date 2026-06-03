module riscv_instruction_memory (
    input  logic        clk,
    input  logic        prog_we,
    input  logic [4:0]  prog_addr,
    input  logic [31:0] prog_wdata,
    input  logic [31:0] fetch_pc,
    output logic [31:0] instruction,
    output logic [31:0] prog_rdata
);
    logic [31:0] mem [0:31];

    initial begin
        // Default to a NOP word (all zeros). The decoder's default branch is
        // a no-op + pc_plus4, so a freshly-elaborated CPU spins through unknown
        // opcodes without halting — load a program via the Memory Viewer and
        // tick the clock to actually execute something.
        for (int i = 0; i < 32; i++) begin
            mem[i] = 32'h00000000;
        end
    end

    assign instruction = mem[fetch_pc[6:2]];
    assign prog_rdata = mem[prog_addr];

    always_ff @(posedge clk) begin
        if (prog_we) begin
            mem[prog_addr] <= prog_wdata;
        end
    end
endmodule

module riscv_decoder (
    input  logic [31:0] instruction,
    input  logic [31:0] pc,
    input  logic [31:0] rs1_value,
    input  logic [31:0] rs2_value,
    output logic [4:0]  rd,
    output logic [4:0]  rs1,
    output logic [4:0]  rs2,
    output logic [31:0] imm_i,
    output logic [31:0] imm_s,
    output logic [31:0] imm_b,
    output logic [31:0] imm_j,
    output logic [31:0] alu_rhs,
    output logic [31:0] next_pc,
    output logic [31:0] pc_plus4,
    output logic [1:0]  writeback_sel,
    output logic        reg_write,
    output logic        mem_write,
    output logic        halt_next
);
    localparam logic [6:0] OPCODE_OP     = 7'b0110011;
    localparam logic [6:0] OPCODE_OP_IMM = 7'b0010011;
    localparam logic [6:0] OPCODE_LOAD   = 7'b0000011;
    localparam logic [6:0] OPCODE_STORE  = 7'b0100011;
    localparam logic [6:0] OPCODE_BRANCH = 7'b1100011;
    localparam logic [6:0] OPCODE_JAL    = 7'b1101111;
    localparam logic [6:0] OPCODE_SYSTEM = 7'b1110011;

    localparam logic [1:0] WB_ALU  = 2'd0;
    localparam logic [1:0] WB_LOAD = 2'd1;
    localparam logic [1:0] WB_PC4  = 2'd2;

    logic [6:0] opcode;
    logic [2:0] funct3;
    logic [6:0] funct7;

    assign opcode = instruction[6:0];
    assign rd = instruction[11:7];
    assign funct3 = instruction[14:12];
    assign rs1 = instruction[19:15];
    assign rs2 = instruction[24:20];
    assign funct7 = instruction[31:25];
    assign imm_i = {{20{instruction[31]}}, instruction[31:20]};
    assign imm_s = {{20{instruction[31]}}, instruction[31:25], instruction[11:7]};
    assign imm_b = {{19{instruction[31]}}, instruction[31], instruction[7], instruction[30:25], instruction[11:8], 1'b0};
    assign imm_j = {{11{instruction[31]}}, instruction[31], instruction[19:12], instruction[20], instruction[30:21], 1'b0};
    assign pc_plus4 = pc + 32'd4;

    always_comb begin
        alu_rhs = rs2_value;
        next_pc = pc_plus4;
        writeback_sel = WB_ALU;
        reg_write = 1'b0;
        mem_write = 1'b0;
        halt_next = 1'b0;

        unique case (opcode)
            OPCODE_OP_IMM: begin
                if (funct3 == 3'b000) begin
                    alu_rhs = imm_i;
                    reg_write = 1'b1;
                end
            end
            OPCODE_OP: begin
                if (funct3 == 3'b000 && funct7 == 7'b0000000) begin
                    reg_write = 1'b1;
                end
            end
            OPCODE_LOAD: begin
                if (funct3 == 3'b010) begin
                    alu_rhs = imm_i;
                    writeback_sel = WB_LOAD;
                    reg_write = 1'b1;
                end
            end
            OPCODE_STORE: begin
                if (funct3 == 3'b010) begin
                    alu_rhs = imm_s;
                    mem_write = 1'b1;
                end
            end
            OPCODE_BRANCH: begin
                if (funct3 == 3'b000 && rs1_value == rs2_value) begin
                    next_pc = pc + imm_b;
                end
            end
            OPCODE_JAL: begin
                writeback_sel = WB_PC4;
                reg_write = 1'b1;
                next_pc = pc + imm_j;
            end
            OPCODE_SYSTEM: begin
                halt_next = instruction == 32'h00100073;
                next_pc = pc;
            end
            default: begin
                // Unknown opcodes are treated as no-ops so a freshly-reset
                // CPU running against an empty instruction memory simply
                // advances PC instead of halting. Only the explicit ebreak
                // path (OPCODE_SYSTEM above) raises halt_next now.
                halt_next = 1'b0;
                next_pc = pc_plus4;
            end
        endcase
    end
endmodule

module riscv_alu (
    input  logic [31:0] lhs,
    input  logic [31:0] rhs,
    output logic [31:0] result,
    output logic        zero
);
    assign result = lhs + rhs;
    assign zero = result == 32'h0;
endmodule

module riscv_register_file (
    input  logic        clk,
    input  logic        rst_n,
    input  logic        enable,
    input  logic        reg_write,
    input  logic [4:0]  rd,
    input  logic [4:0]  rs1,
    input  logic [4:0]  rs2,
    input  logic [31:0] write_data,
    output logic [31:0] rs1_value,
    output logic [31:0] rs2_value,
    output logic [31:0] debug_x1,
    output logic [31:0] debug_x2,
    output logic [31:0] debug_x3,
    output logic [31:0] debug_x4,
    output logic [31:0] debug_x5,
    output logic [31:0] debug_x6
);
    logic [31:0] regs [0:31];

    assign rs1_value = rs1 == 5'd0 ? 32'h0 : regs[rs1];
    assign rs2_value = rs2 == 5'd0 ? 32'h0 : regs[rs2];
    assign debug_x1 = regs[1];
    assign debug_x2 = regs[2];
    assign debug_x3 = regs[3];
    assign debug_x4 = regs[4];
    assign debug_x5 = regs[5];
    assign debug_x6 = regs[6];

    always_ff @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            for (int i = 0; i < 32; i++) begin
                regs[i] <= 32'h0;
            end
        end else if (enable) begin
            if (reg_write && rd != 5'd0) begin
                regs[rd] <= write_data;
            end
            regs[0] <= 32'h0;
        end
    end
endmodule

module riscv_data_memory (
    input  logic        clk,
    input  logic        rst_n,
    input  logic        mem_write,
    input  logic [31:0] address,
    input  logic [31:0] write_data,
    output logic [31:0] read_data,
    output logic [31:0] debug_dmem0
);
    logic [31:0] mem [0:31];

    assign read_data = mem[address[6:2]];
    assign debug_dmem0 = mem[0];

    always_ff @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            for (int i = 0; i < 32; i++) begin
                mem[i] <= 32'h0;
            end
        end else if (mem_write) begin
            mem[address[6:2]] <= write_data;
        end
    end
endmodule

module riscv_single_cycle_top (
    input  logic        clk,
    input  logic        rst_n,
    input  logic        enable,
    input  logic        prog_we,
    input  logic [4:0]  prog_addr,
    input  logic [31:0] prog_wdata,
    output logic [31:0] prog_rdata,
    output logic [31:0] pc,
    output logic [31:0] instruction,
    output logic [31:0] debug_x1,
    output logic [31:0] debug_x2,
    output logic [31:0] debug_x3,
    output logic [31:0] debug_x4,
    output logic [31:0] debug_x5,
    output logic [31:0] debug_x6,
    output logic [31:0] debug_dmem0,
    output logic        halted
);
    localparam logic [1:0] WB_ALU  = 2'd0;
    localparam logic [1:0] WB_LOAD = 2'd1;
    localparam logic [1:0] WB_PC4  = 2'd2;

    logic [4:0]  rd;
    logic [4:0]  rs1;
    logic [4:0]  rs2;
    logic [31:0] rs1_value;
    logic [31:0] rs2_value;
    logic [31:0] imm_i;
    logic [31:0] imm_s;
    logic [31:0] imm_b;
    logic [31:0] imm_j;
    logic [31:0] alu_rhs;
    logic [31:0] alu_result;
    logic [31:0] next_pc;
    logic [31:0] pc_plus4;
    logic [31:0] load_data;
    logic [31:0] write_data;
    logic [1:0]  writeback_sel;
    logic        reg_write;
    logic        mem_write;
    logic        halt_next;
    logic        alu_zero;

    riscv_instruction_memory u_imem (
        .clk(clk),
        .prog_we(prog_we),
        .prog_addr(prog_addr),
        .prog_wdata(prog_wdata),
        .fetch_pc(pc),
        .instruction(instruction),
        .prog_rdata(prog_rdata)
    );

    riscv_decoder u_decoder (
        .instruction(instruction),
        .pc(pc),
        .rs1_value(rs1_value),
        .rs2_value(rs2_value),
        .rd(rd),
        .rs1(rs1),
        .rs2(rs2),
        .imm_i(imm_i),
        .imm_s(imm_s),
        .imm_b(imm_b),
        .imm_j(imm_j),
        .alu_rhs(alu_rhs),
        .next_pc(next_pc),
        .pc_plus4(pc_plus4),
        .writeback_sel(writeback_sel),
        .reg_write(reg_write),
        .mem_write(mem_write),
        .halt_next(halt_next)
    );

    riscv_register_file u_registers (
        .clk(clk),
        .rst_n(rst_n),
        .enable(enable && !halted),
        .reg_write(reg_write),
        .rd(rd),
        .rs1(rs1),
        .rs2(rs2),
        .write_data(write_data),
        .rs1_value(rs1_value),
        .rs2_value(rs2_value),
        .debug_x1(debug_x1),
        .debug_x2(debug_x2),
        .debug_x3(debug_x3),
        .debug_x4(debug_x4),
        .debug_x5(debug_x5),
        .debug_x6(debug_x6)
    );

    riscv_alu u_alu (
        .lhs(rs1_value),
        .rhs(alu_rhs),
        .result(alu_result),
        .zero(alu_zero)
    );

    riscv_data_memory u_dmem (
        .clk(clk),
        .rst_n(rst_n),
        .mem_write(enable && !halted && mem_write),
        .address(alu_result),
        .write_data(rs2_value),
        .read_data(load_data),
        .debug_dmem0(debug_dmem0)
    );

    always_comb begin
        unique case (writeback_sel)
            WB_LOAD: write_data = load_data;
            WB_PC4:  write_data = pc_plus4;
            WB_ALU:  write_data = alu_result;
            default: write_data = alu_result;
        endcase
    end

    always_ff @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            pc <= 32'h0;
            halted <= 1'b0;
        end else if (enable && !halted) begin
            pc <= next_pc;
            if (halt_next) begin
                halted <= 1'b1;
            end
        end
    end
endmodule
