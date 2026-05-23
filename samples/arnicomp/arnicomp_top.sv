import control_pkg::*;

// ArniComp 8-bit CPU core — external instruction and data memory interface.
// instruction: current instruction byte (driven externally, e.g. by a ROM model or test bench).
// pc_out:      program counter output so the user can observe or drive the fetch address.
// mem_rdata:   data bus read from external RAM.
// mem_addr/mem_wdata/mem_wen/mem_ren: data memory bus.
module arnicomp_top #(
    parameter logic [15:0] STACK_PTR_RESET_VALUE = 16'h0000
)(
    input  logic        clk,
    input  logic        rst_n,
    input  logic [7:0]  instruction,
    input  logic [7:0]  mem_rdata,

    output logic [15:0] pc_out,
    output logic [15:0] mem_addr,
    output logic [7:0]  mem_wdata,
    output logic        mem_wen,
    output logic        mem_ren
);

    // -----------------------------------------------------------------------
    // Internal signals
    // -----------------------------------------------------------------------
    logic        jump_taken;
    logic [2:0]  jmp_select;
    logic        flush_next_instr;

    logic [7:0]  inst_q;
    logic [7:0]  exec_instr;

    logic        zero_flag;
    logic        negative_flag;
    logic        carry_flag;
    logic        overflow_flag;

    logic        z_reg_out;
    logic        n_reg_out;
    logic        c_reg_out;
    logic        v_reg_out;

    logic [7:0]  alu_out;
    logic [7:0]  reg_a_out;
    logic [7:0]  reg_b_out;
    logic [7:0]  reg_d_out;
    logic [7:0]  acc_out;
    logic [7:0]  marl_out;
    logic [7:0]  marh_out;
    logic [7:0]  prl_out;
    logic [7:0]  prh_out;
    logic [7:0]  lrl_out;
    logic [7:0]  lrh_out;
    logic [15:0] lr_out;
    logic [15:0] pc_addr;
    logic [15:0] sp_out;
    logic [15:0] active_stack_addr;
    logic        is_push_instr;
    logic [7:0]  stack_wdata;

    logic        reg_a_we;
    logic        reg_b_we;
    logic        reg_d_we;
    logic        acc_we;
    logic        marl_we;
    logic        marh_we;
    logic        prl_we;
    logic        prh_we;
    logic        mem_we;

    logic [7:0]  bus;
    logic [7:0]  ldh_bus;
    logic [7:0]  bus_sel_out;
    logic        alu_carry_in;

    control_pkg::ctrl_t control_pins;

    // -----------------------------------------------------------------------
    // Address / data bus decode
    // -----------------------------------------------------------------------
    assign active_stack_addr = control_pins.inc_dec_sel ? (sp_out - 16'd1) : sp_out;
    assign is_push_instr     = exec_instr[7:3] == 5'b00100;
    assign mem_addr          = control_pins.sp_sel ? active_stack_addr : {marh_out, marl_out};

    // PUSH remaps 100→MARH and 111→MARL so the stack saves both halves correctly.
    assign stack_wdata =
        (is_push_instr && control_pins.ssel == 3'b100) ? marh_out :
        (is_push_instr && control_pins.ssel == 3'b111) ? marl_out :
        bus;

    assign mem_wdata = control_pins.sp_sel ? stack_wdata : bus;
    assign mem_wen   = mem_we;
    assign mem_ren   = control_pins.oe && (control_pins.ssel == 3'b111) && !is_push_instr;

    // -----------------------------------------------------------------------
    // Flush logic — hides the fetch-delay slot after a taken jump
    // -----------------------------------------------------------------------
    always_ff @(posedge clk or negedge rst_n) begin
        if (!rst_n) flush_next_instr <= 1'b0;
        else        flush_next_instr <= jump_taken;
    end

    // instruction is driven externally; register it to align with the
    // synchronous PC so fetch and decode stay in step.
    always_ff @(posedge clk or negedge rst_n) begin
        if (!rst_n) inst_q <= 8'h00;
        else        inst_q <= instruction;
    end

    assign exec_instr = flush_next_instr ? 8'h00 : inst_q;

    // -----------------------------------------------------------------------
    // Immediate / bus formation
    // -----------------------------------------------------------------------
    assign ldh_bus = (control_pins.dsel == 3'b001)
                   ? {inst_q[2:0], reg_d_out[4:0]}
                   : {inst_q[2:0], reg_a_out[4:0]};

    assign bus = control_pins.im5_en      ? {3'b0, inst_q[4:0]}  :
                 control_pins.im3_low_en  ? {5'b0, inst_q[2:0]}  :
                 control_pins.im3_high_en ? ldh_bus               :
                 bus_sel_out;

    assign pc_out = pc_addr;

    // -----------------------------------------------------------------------
    // Sub-modules
    // -----------------------------------------------------------------------
    bus_selector bus_selector_i (
        .sel(control_pins.ssel),
        .out_en(control_pins.oe),
        .a(reg_a_out),
        .d(reg_d_out),
        .b(reg_b_out),
        .acc(acc_out),
        .lrl(lrl_out),
        .lrh(lrh_out),
        .m(mem_rdata),
        .out(bus_sel_out)
    );

    reg_a reg_a_i (
        .clk(clk),
        .rst_n(rst_n),
        .we(reg_a_we),
        .smsbra(1'b0),
        .d(bus),
        .out(reg_a_out)
    );

    reg_cell #(.W(8)) reg_b (
        .clk(clk), .rst_n(rst_n), .we(reg_b_we),
        .oe(1'b1), .d(bus), .out(reg_b_out)
    );

    reg_cell #(.W(8)) reg_d (
        .clk(clk), .rst_n(rst_n), .we(reg_d_we),
        .oe(1'b1), .d(bus), .out(reg_d_out)
    );

    reg_cell #(.W(8)) acc (
        .clk(clk), .rst_n(rst_n), .we(acc_we),
        .oe(1'b1), .d(alu_out), .out(acc_out)
    );

    reg_marl marl_i (
        .clk(clk),
        .rst_n(rst_n),
        .marl_we(marl_we),
        .marh_we(marh_we),
        .inc(control_pins.inc_mar),
        .inc_dec_sel(control_pins.inc_dec_sel),
        .inc_by_two(inst_q[0]),
        .d(bus),
        .marl_out(marl_out),
        .marh_out(marh_out)
    );

    reg_cell #(.W(8)) prl (
        .clk(clk), .rst_n(rst_n), .we(prl_we),
        .oe(1'b1), .d(bus), .out(prl_out)
    );

    reg_cell #(.W(8)) prh (
        .clk(clk), .rst_n(rst_n), .we(prh_we),
        .oe(1'b1), .d(bus), .out(prh_out)
    );

    reg_cell #(.W(16)) lr (
        .clk(clk), .rst_n(rst_n), .we(control_pins.set_lr),
        .oe(1'b1), .d(pc_addr), .out(lr_out)
    );

    assign lrl_out = lr_out[7:0];
    assign lrh_out = lr_out[15:8];

    stack_pointer #(
        .RESET_VALUE(STACK_PTR_RESET_VALUE)
    ) sp_i (
        .clk(clk),
        .rst_n(rst_n),
        .update_en(control_pins.sp_sel),
        .inc_dec_sel(control_pins.inc_dec_sel),
        .out(sp_out)
    );

    flag_reg flag_reg_i (
        .clk(clk),
        .rst_n(rst_n),
        .we(control_pins.sf),
        .z_f_in(zero_flag),
        .n_f_in(negative_flag),
        .c_f_in(carry_flag),
        .v_f_in(overflow_flag),
        .z_f_out(z_reg_out),
        .n_f_out(n_reg_out),
        .c_f_out(c_reg_out),
        .v_f_out(v_reg_out)
    );

    program_counter pc (
        .clk(clk),
        .rst_n(rst_n),
        .count_en(control_pins.ce),
        .load_en(jump_taken),
        .load_data({prh_out, prl_out}),
        .paddr(pc_addr)
    );

    control_decoder instruction_decoder (
        .instr_in(exec_instr),
        .ctrl_pkg_out(control_pins),
        .jmp_sel(jmp_select),
        .reg_a_we(reg_a_we),
        .reg_b_we(reg_b_we),
        .reg_d_we(reg_d_we),
        .marl_we(marl_we),
        .marh_we(marh_we),
        .prl_we(prl_we),
        .prh_we(prh_we),
        .mem_we(mem_we),
        .acc_we(acc_we)
    );

    assign alu_carry_in = control_pins.sc ? c_reg_out : control_pins.sn;

    alu alu_i (
        .a(reg_d_out),
        .b(bus),
        .ops(control_pins.ops),
        .negative(control_pins.sn),
        .c_in(alu_carry_in),
        .result(alu_out),
        .zero_flag(zero_flag),
        .negative_flag(negative_flag),
        .carry_flag(carry_flag),
        .overflow_flag(overflow_flag)
    );

    jump_logic jump_decoder (
        .jmp_en(control_pins.jmp),
        .jgt(control_pins.jgt),
        .zero_flag(z_reg_out),
        .negative_flag(n_reg_out),
        .overflow_flag(v_reg_out),
        .carry_flag(c_reg_out),
        .jmp_cond(jmp_select),
        .jmp_taken(jump_taken)
    );

endmodule
