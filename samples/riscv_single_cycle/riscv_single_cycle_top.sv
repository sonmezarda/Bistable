module riscv_single_cycle_top (
    input  logic        clk,
    input  logic        rst_n,
    input  logic        enable,
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
    localparam logic [6:0] OPCODE_OP     = 7'b0110011;
    localparam logic [6:0] OPCODE_OP_IMM = 7'b0010011;
    localparam logic [6:0] OPCODE_LOAD   = 7'b0000011;
    localparam logic [6:0] OPCODE_STORE  = 7'b0100011;
    localparam logic [6:0] OPCODE_BRANCH = 7'b1100011;
    localparam logic [6:0] OPCODE_JAL    = 7'b1101111;
    localparam logic [6:0] OPCODE_SYSTEM = 7'b1110011;

    logic [31:0] regs [0:31];
    logic [31:0] imem [0:31];
    logic [31:0] dmem [0:31];

    logic [4:0]  rd;
    logic [4:0]  rs1;
    logic [4:0]  rs2;
    logic [2:0]  funct3;
    logic [6:0]  funct7;
    logic [6:0]  opcode;
    logic [31:0] rs1_value;
    logic [31:0] rs2_value;
    logic [31:0] imm_i;
    logic [31:0] imm_s;
    logic [31:0] imm_b;
    logic [31:0] imm_j;
    logic [31:0] next_pc;
    logic [31:0] writeback;
    logic [31:0] load_data;
    logic [31:0] load_address;
    logic [31:0] data_address;
    logic        reg_write;
    logic        mem_write;
    logic        halt_next;

    initial begin
        imem[0]  = 32'h00500093; // addi x1, x0, 5
        imem[1]  = 32'h00700113; // addi x2, x0, 7
        imem[2]  = 32'h002081b3; // add  x3, x1, x2
        imem[3]  = 32'h00302023; // sw   x3, 0(x0)
        imem[4]  = 32'h00002203; // lw   x4, 0(x0)
        imem[5]  = 32'h00320463; // beq  x4, x3, +8
        imem[6]  = 32'h00100293; // addi x5, x0, 1   (skipped)
        imem[7]  = 32'h02a00293; // addi x5, x0, 42
        imem[8]  = 32'h0080036f; // jal  x6, +8
        imem[9]  = 32'h06300293; // addi x5, x0, 99  (skipped)
        imem[10] = 32'h00100073; // ebreak
        for (int i = 11; i < 32; i++) begin
            imem[i] = 32'h00100073;
        end
    end

    assign instruction = imem[pc[6:2]];
    assign opcode = instruction[6:0];
    assign rd = instruction[11:7];
    assign funct3 = instruction[14:12];
    assign rs1 = instruction[19:15];
    assign rs2 = instruction[24:20];
    assign funct7 = instruction[31:25];
    assign rs1_value = rs1 == 5'd0 ? 32'h0 : regs[rs1];
    assign rs2_value = rs2 == 5'd0 ? 32'h0 : regs[rs2];
    assign imm_i = {{20{instruction[31]}}, instruction[31:20]};
    assign imm_s = {{20{instruction[31]}}, instruction[31:25], instruction[11:7]};
    assign imm_b = {{19{instruction[31]}}, instruction[31], instruction[7], instruction[30:25], instruction[11:8], 1'b0};
    assign imm_j = {{11{instruction[31]}}, instruction[31], instruction[19:12], instruction[20], instruction[30:21], 1'b0};
    assign data_address = rs1_value + imm_s;
    assign load_address = rs1_value + imm_i;
    assign load_data = dmem[load_address[6:2]];

    assign debug_x1 = regs[1];
    assign debug_x2 = regs[2];
    assign debug_x3 = regs[3];
    assign debug_x4 = regs[4];
    assign debug_x5 = regs[5];
    assign debug_x6 = regs[6];
    assign debug_dmem0 = dmem[0];

    always_comb begin
        next_pc = pc + 32'd4;
        writeback = 32'h0;
        reg_write = 1'b0;
        mem_write = 1'b0;
        halt_next = 1'b0;

        unique case (opcode)
            OPCODE_OP_IMM: begin
                if (funct3 == 3'b000) begin
                    writeback = rs1_value + imm_i;
                    reg_write = 1'b1;
                end
            end
            OPCODE_OP: begin
                if (funct3 == 3'b000 && funct7 == 7'b0000000) begin
                    writeback = rs1_value + rs2_value;
                    reg_write = 1'b1;
                end
            end
            OPCODE_LOAD: begin
                if (funct3 == 3'b010) begin
                    writeback = load_data;
                    reg_write = 1'b1;
                end
            end
            OPCODE_STORE: begin
                if (funct3 == 3'b010) begin
                    mem_write = 1'b1;
                end
            end
            OPCODE_BRANCH: begin
                if (funct3 == 3'b000 && rs1_value == rs2_value) begin
                    next_pc = pc + imm_b;
                end
            end
            OPCODE_JAL: begin
                writeback = pc + 32'd4;
                reg_write = 1'b1;
                next_pc = pc + imm_j;
            end
            OPCODE_SYSTEM: begin
                halt_next = instruction == 32'h00100073;
                next_pc = pc;
            end
            default: begin
                halt_next = 1'b1;
                next_pc = pc;
            end
        endcase
    end

    always_ff @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            pc <= 32'h0;
            halted <= 1'b0;
            for (int i = 0; i < 32; i++) begin
                regs[i] <= 32'h0;
                dmem[i] <= 32'h0;
            end
        end else if (enable && !halted) begin
            if (mem_write) begin
                dmem[data_address[6:2]] <= rs2_value;
            end
            if (reg_write && rd != 5'd0) begin
                regs[rd] <= writeback;
            end
            regs[0] <= 32'h0;
            pc <= next_pc;
            if (halt_next) begin
                halted <= 1'b1;
            end
        end
    end
endmodule
