module tiny_cpu_top (
    input  logic        clk,
    input  logic        rst_n,
    input  logic        enable,
    input  logic        irq,
    input  logic [15:0] instruction,
    input  logic [7:0]  data_in,
    output logic [7:0]  pc,
    output logic [7:0]  acc,
    output logic [7:0]  mem_addr,
    output logic        mem_write,
    output logic        halted
);
    logic [3:0] opcode;
    logic [7:0] immediate;
    logic [2:0] alu_op;
    logic       reg_write;
    logic       pc_load;
    logic       use_imm;
    logic       halt_next;
    logic [7:0] alu_result;
    logic       zero_i;
    logic       carry_i;
    logic       irq_pending;

    assign opcode = instruction[15:12];
    assign immediate = instruction[7:0];
    assign mem_addr = pc + immediate;

    control_unit u_control (
        .opcode(opcode),
        .zero_flag(zero_i),
        .irq_pending(irq_pending),
        .alu_op(alu_op),
        .reg_write(reg_write),
        .pc_load(pc_load),
        .mem_write(mem_write),
        .use_imm(use_imm),
        .halt_next(halt_next)
    );

    register_file u_registers (
        .clk(clk),
        .rst_n(rst_n),
        .enable(enable),
        .reg_write(reg_write),
        .pc_load(pc_load),
        .alu_result(alu_result),
        .immediate(immediate),
        .pc(pc),
        .acc(acc)
    );

    alu8 u_alu (
        .acc(acc),
        .data_in(data_in),
        .immediate(immediate),
        .use_imm(use_imm),
        .alu_op(alu_op),
        .result(alu_result),
        .zero(zero_i),
        .carry(carry_i)
    );

    status_flags u_status (
        .clk(clk),
        .rst_n(rst_n),
        .enable(enable),
        .irq(irq),
        .zero_in(zero_i),
        .carry_in(carry_i),
        .halt_next(halt_next),
        .irq_pending(irq_pending),
        .halted(halted)
    );
endmodule
