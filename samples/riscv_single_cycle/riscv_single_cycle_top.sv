// RV32I single-cycle core (base integer ISA).
//
// Simplest fully-working RV32I: every base-ISA instruction decodes and executes.
// Deliberate simplification: loads/stores are WORD-only — byte/half opcodes
// (LB/LH/LBU/LHU/SB/SH) decode but access a full 32-bit word. Everything else
// (all OP / OP-IMM / LUI / AUIPC / BRANCH / JAL / JALR / ebreak) is complete.
//
// Out of scope: CSRs, FENCE, traps/exceptions, misaligned faults, M/A/F.

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
        // Default to a NOP word (all zeros). Unknown opcodes advance PC, so a
        // freshly-elaborated CPU spins harmlessly until a program is loaded.
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

// ── ALU operation encoding (shared decoder ↔ ALU contract) ──────────────────
//   0 ADD   1 SUB   2 SLL   3 SLT   4 SLTU   5 XOR   6 SRL   7 SRA   8 OR   9 AND
//   (LUI passes rhs through as ALU_OR with lhs=0; AUIPC/JAL/branch targets use ADD)

module riscv_alu (
    input  logic [31:0] lhs,
    input  logic [31:0] rhs,
    input  logic [3:0]  alu_op,
    output logic [31:0] result,
    output logic        zero
);
    localparam logic [3:0] ALU_ADD  = 4'd0;
    localparam logic [3:0] ALU_SUB  = 4'd1;
    localparam logic [3:0] ALU_SLL  = 4'd2;
    localparam logic [3:0] ALU_SLT  = 4'd3;
    localparam logic [3:0] ALU_SLTU = 4'd4;
    localparam logic [3:0] ALU_XOR  = 4'd5;
    localparam logic [3:0] ALU_SRL  = 4'd6;
    localparam logic [3:0] ALU_SRA  = 4'd7;
    localparam logic [3:0] ALU_OR   = 4'd8;
    localparam logic [3:0] ALU_AND  = 4'd9;

    // Signed views so `<` and `>>>` are arithmetic. Declaring the wires signed
    // avoids $signed()/$unsigned() casts (unsupported by the schematic decoder).
    logic signed [31:0] lhs_s;
    logic signed [31:0] rhs_s;
    assign lhs_s = lhs;
    assign rhs_s = rhs;

    logic [4:0] shamt;
    assign shamt = rhs[4:0];

    always_comb begin
        unique case (alu_op)
            ALU_ADD:  result = lhs + rhs;
            ALU_SUB:  result = lhs - rhs;
            ALU_SLL:  result = lhs << shamt;
            ALU_SLT:  result = (lhs_s < rhs_s) ? 32'd1 : 32'd0;
            ALU_SLTU: result = (lhs < rhs) ? 32'd1 : 32'd0;
            ALU_XOR:  result = lhs ^ rhs;
            ALU_SRL:  result = lhs >> shamt;
            ALU_SRA:  result = lhs_s >>> shamt;
            ALU_OR:   result = lhs | rhs;
            ALU_AND:  result = lhs & rhs;
            default:  result = lhs + rhs;
        endcase
    end

    assign zero = result == 32'h0;
endmodule

module riscv_decoder (
    input  logic [31:0] instruction,
    output logic [4:0]  rd,
    output logic [4:0]  rs1,
    output logic [4:0]  rs2,
    output logic [3:0]  alu_op,       // ALU function select
    output logic        alu_src_a,    // 0 = rs1, 1 = pc
    output logic        alu_src_b,    // 0 = rs2, 1 = imm
    output logic [31:0] alu_imm,      // immediate chosen for the ALU B input
    output logic [31:0] imm_branch,   // B-immediate (branch target offset)
    output logic [31:0] imm_jal,      // J-immediate (JAL target offset)
    output logic [2:0]  funct3,       // for branch-condition resolution in top
    output logic        is_branch,
    output logic        is_jal,
    output logic        is_jalr,
    output logic [2:0]  writeback_sel,
    output logic        reg_write,
    output logic        mem_write,
    output logic        halt_next
);
    localparam logic [6:0] OPCODE_LUI    = 7'b0110111;
    localparam logic [6:0] OPCODE_AUIPC  = 7'b0010111;
    localparam logic [6:0] OPCODE_JAL    = 7'b1101111;
    localparam logic [6:0] OPCODE_JALR   = 7'b1100111;
    localparam logic [6:0] OPCODE_BRANCH = 7'b1100011;
    localparam logic [6:0] OPCODE_LOAD   = 7'b0000011;
    localparam logic [6:0] OPCODE_STORE  = 7'b0100011;
    localparam logic [6:0] OPCODE_OP_IMM = 7'b0010011;
    localparam logic [6:0] OPCODE_OP     = 7'b0110011;
    localparam logic [6:0] OPCODE_SYSTEM = 7'b1110011;

    // ALU op encoding — must match riscv_alu.
    localparam logic [3:0] ALU_ADD  = 4'd0;
    localparam logic [3:0] ALU_SUB  = 4'd1;
    localparam logic [3:0] ALU_SLL  = 4'd2;
    localparam logic [3:0] ALU_SLT  = 4'd3;
    localparam logic [3:0] ALU_SLTU = 4'd4;
    localparam logic [3:0] ALU_XOR  = 4'd5;
    localparam logic [3:0] ALU_SRL  = 4'd6;
    localparam logic [3:0] ALU_SRA  = 4'd7;
    localparam logic [3:0] ALU_OR   = 4'd8;
    localparam logic [3:0] ALU_AND  = 4'd9;

    // Writeback source select.
    localparam logic [2:0] WB_ALU  = 3'd0;
    localparam logic [2:0] WB_LOAD = 3'd1;
    localparam logic [2:0] WB_PC4  = 3'd2;
    localparam logic [2:0] WB_IMM  = 3'd3; // LUI: pass the U-immediate through

    logic [6:0] opcode;
    logic [6:0] funct7;

    assign opcode = instruction[6:0];
    assign rd = instruction[11:7];
    assign funct3 = instruction[14:12];
    assign rs1 = instruction[19:15];
    assign rs2 = instruction[24:20];
    assign funct7 = instruction[31:25];

    // Immediates used by the datapath. I/S share the ALU B input via alu_imm;
    // branch/JAL/U leave the decoder for PC and writeback muxing in top.
    logic [31:0] imm_i;
    logic [31:0] imm_s;
    logic [31:0] imm_u;
    assign imm_i      = {{20{instruction[31]}}, instruction[31:20]};
    assign imm_s      = {{20{instruction[31]}}, instruction[31:25], instruction[11:7]};
    assign imm_branch = {{19{instruction[31]}}, instruction[31], instruction[7], instruction[30:25], instruction[11:8], 1'b0};
    assign imm_u      = {instruction[31:12], 12'h000};
    assign imm_jal    = {{11{instruction[31]}}, instruction[31], instruction[19:12], instruction[20], instruction[30:21], 1'b0};

    assign is_branch = (opcode == OPCODE_BRANCH);
    assign is_jal    = (opcode == OPCODE_JAL);
    assign is_jalr   = (opcode == OPCODE_JALR);

    // Register-register / register-immediate ALU function from funct3 (+funct7[5]).
    // is_reg_op distinguishes OP (SUB/SRA legal) from OP-IMM (only SRAI via funct7).
    logic is_reg_op;
    assign is_reg_op = (opcode == OPCODE_OP);

    logic [3:0] arith_op;
    always_comb begin
        unique case (funct3)
            3'b000:  arith_op = (is_reg_op && funct7[5]) ? ALU_SUB : ALU_ADD; // ADD/SUB (ADDI = ADD)
            3'b001:  arith_op = ALU_SLL;                                      // SLL/SLLI
            3'b010:  arith_op = ALU_SLT;                                      // SLT/SLTI
            3'b011:  arith_op = ALU_SLTU;                                     // SLTU/SLTIU
            3'b100:  arith_op = ALU_XOR;                                      // XOR/XORI
            3'b101:  arith_op = funct7[5] ? ALU_SRA : ALU_SRL;               // SRL/SRA (SRLI/SRAI)
            3'b110:  arith_op = ALU_OR;                                       // OR/ORI
            3'b111:  arith_op = ALU_AND;                                      // AND/ANDI
            default: arith_op = ALU_ADD;
        endcase
    end

    // ALU op for branches: the ALU computes the comparison so its zero/result
    // outputs (consumed in top) resolve the branch. BEQ/BNE→SUB, BLT/BGE→SLT,
    // BLTU/BGEU→SLTU.
    logic [3:0] branch_alu_op;
    always_comb begin
        unique case (funct3)
            3'b000, 3'b001: branch_alu_op = ALU_SUB;  // BEQ / BNE  → zero flag
            3'b100, 3'b101: branch_alu_op = ALU_SLT;  // BLT / BGE  → signed  <
            3'b110, 3'b111: branch_alu_op = ALU_SLTU; // BLTU/ BGEU → unsigned <
            default:        branch_alu_op = ALU_SUB;
        endcase
    end

    always_comb begin
        // Defaults: NOP-like — write nothing, ALU adds rs1+rs2.
        alu_op        = ALU_ADD;
        alu_src_a     = 1'b0;      // rs1
        alu_src_b     = 1'b0;      // rs2
        alu_imm       = imm_i;
        writeback_sel = WB_ALU;
        reg_write     = 1'b0;
        mem_write     = 1'b0;
        halt_next     = 1'b0;

        unique case (opcode)
            OPCODE_OP_IMM: begin
                alu_op    = arith_op;
                alu_src_b = 1'b1;   // immediate
                alu_imm   = imm_i;
                reg_write = 1'b1;
            end
            OPCODE_OP: begin
                alu_op    = arith_op;
                alu_src_b = 1'b0;   // rs2
                reg_write = 1'b1;
            end
            OPCODE_LUI: begin
                writeback_sel = WB_IMM;
                alu_imm       = imm_u;
                reg_write     = 1'b1;
            end
            OPCODE_AUIPC: begin
                alu_op    = ALU_ADD;
                alu_src_a = 1'b1;   // pc
                alu_src_b = 1'b1;   // imm_u
                alu_imm   = imm_u;
                reg_write = 1'b1;
            end
            OPCODE_LOAD: begin
                // Word-only: address = rs1 + imm_i (byte/half decode as word).
                alu_op        = ALU_ADD;
                alu_src_b     = 1'b1;
                alu_imm       = imm_i;
                writeback_sel = WB_LOAD;
                reg_write     = 1'b1;
            end
            OPCODE_STORE: begin
                alu_op    = ALU_ADD;
                alu_src_b = 1'b1;
                alu_imm   = imm_s;
                mem_write = 1'b1;
            end
            OPCODE_BRANCH: begin
                // ALU compares rs1 vs rs2; top resolves taken from zero/result.
                alu_op    = branch_alu_op;
                alu_src_b = 1'b0;   // rs2
            end
            OPCODE_JAL: begin
                writeback_sel = WB_PC4;
                reg_write     = 1'b1;
            end
            OPCODE_JALR: begin
                // Target = rs1 + imm_i (ALU adds); top clears bit 0.
                alu_op        = ALU_ADD;
                alu_src_b     = 1'b1;
                alu_imm       = imm_i;
                writeback_sel = WB_PC4;
                reg_write     = 1'b1;
            end
            OPCODE_SYSTEM: begin
                halt_next = instruction == 32'h00100073; // ebreak
            end
            default: begin
                // Unknown opcodes are no-ops.
            end
        endcase
    end
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

    // Word-addressed: index by address[6:2] (loads/stores are word-only).
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
    localparam logic [2:0] WB_ALU  = 3'd0;
    localparam logic [2:0] WB_LOAD = 3'd1;
    localparam logic [2:0] WB_PC4  = 3'd2;
    localparam logic [2:0] WB_IMM  = 3'd3;

    logic [4:0]  rd;
    logic [4:0]  rs1;
    logic [4:0]  rs2;
    logic [31:0] rs1_value;
    logic [31:0] rs2_value;
    logic [3:0]  alu_op;
    logic        alu_src_a;
    logic        alu_src_b;
    logic [31:0] alu_imm;
    logic [31:0] imm_branch;
    logic [31:0] imm_jal;
    logic [2:0]  funct3;
    logic        is_branch;
    logic        is_jal;
    logic        is_jalr;
    logic [31:0] alu_lhs;
    logic [31:0] alu_rhs;
    logic [31:0] alu_result;
    logic        alu_zero;
    logic        branch_taken;
    logic [31:0] next_pc;
    logic [31:0] pc_plus4;
    logic [31:0] load_data;
    logic [31:0] write_data;
    logic [2:0]  writeback_sel;
    logic        reg_write;
    logic        mem_write;
    logic        halt_next;

    assign pc_plus4 = pc + 32'd4;

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
        .rd(rd),
        .rs1(rs1),
        .rs2(rs2),
        .alu_op(alu_op),
        .alu_src_a(alu_src_a),
        .alu_src_b(alu_src_b),
        .alu_imm(alu_imm),
        .imm_branch(imm_branch),
        .imm_jal(imm_jal),
        .funct3(funct3),
        .is_branch(is_branch),
        .is_jal(is_jal),
        .is_jalr(is_jalr),
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

    // ALU operand muxes: A = pc (AUIPC) or rs1; B = immediate or rs2.
    assign alu_lhs = alu_src_a ? pc : rs1_value;
    assign alu_rhs = alu_src_b ? alu_imm : rs2_value;

    riscv_alu u_alu (
        .lhs(alu_lhs),
        .rhs(alu_rhs),
        .alu_op(alu_op),
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

    // Branch resolution from the ALU outputs (this is where alu_zero is consumed):
    //   BEQ  taken if zero;         BNE  taken if !zero
    //   BLT/BLTU taken if result[0] (SLT/SLTU set it); BGE/BGEU taken if !result[0]
    always_comb begin
        unique case (funct3)
            3'b000:  branch_taken = alu_zero;        // BEQ
            3'b001:  branch_taken = !alu_zero;       // BNE
            3'b100:  branch_taken = alu_result[0];   // BLT
            3'b101:  branch_taken = !alu_result[0];  // BGE
            3'b110:  branch_taken = alu_result[0];   // BLTU
            3'b111:  branch_taken = !alu_result[0];  // BGEU
            default: branch_taken = 1'b0;
        endcase
    end

    // Next-PC mux: an ebreak freezes PC (halted core), JAL (pc+imm_j), JALR
    // (rs1+imm_i via ALU, bit0 cleared), taken branch (pc+imm_b), else pc+4.
    always_comb begin
        if (halt_next)
            next_pc = pc;
        else if (is_jal)
            next_pc = pc + imm_jal;
        else if (is_jalr)
            next_pc = alu_result & 32'hFFFFFFFE;
        else if (is_branch && branch_taken)
            next_pc = pc + imm_branch;
        else
            next_pc = pc_plus4;
    end

    // Writeback source mux.
    always_comb begin
        unique case (writeback_sel)
            WB_LOAD: write_data = load_data;
            WB_PC4:  write_data = pc_plus4;
            WB_IMM:  write_data = alu_imm;      // LUI passes the U-immediate
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
