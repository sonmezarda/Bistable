module stack_pointer #(
    parameter logic [15:0] RESET_VALUE = 16'h0000
)(
    input  logic        clk,
    input  logic        rst_n,
    input  logic        update_en,
    input  logic        inc_dec_sel,
    output logic [15:0] out
);

    logic [15:0] next_sp;

    always_comb begin
        next_sp = inc_dec_sel ? (out - 16'd1) : (out + 16'd1);
    end

    reg_cell #(
        .W(16),
        .RESET_VALUE(RESET_VALUE)
    ) sp_reg (
        .clk(clk),
        .rst_n(rst_n),
        .we(update_en),
        .oe(1'b1),
        .d(next_sp),
        .out(out)
    );

endmodule
