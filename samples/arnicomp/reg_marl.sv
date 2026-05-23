// 16-bit Memory Address Register — MARL/MARH can be written separately,
// and the full 16-bit value can be incremented or decremented by 1 or 2.
module reg_marl (
    input  logic       clk,
    input  logic       rst_n,
    input  logic       marl_we,
    input  logic       marh_we,
    input  logic       inc,
    input  logic       inc_dec_sel,
    input  logic       inc_by_two,
    input  logic [7:0] d,
    output logic [7:0] marl_out,
    output logic [7:0] marh_out
);

    logic [15:0] mar_q;
    logic [15:0] mar_d;
    logic [15:0] mar_step;

    assign mar_step = inc_by_two ? 16'd2 : 16'd1;

    always_comb begin
        mar_d = mar_q;
        if (inc) begin
            mar_d = inc_dec_sel ? (mar_q - mar_step) : (mar_q + mar_step);
        end else begin
            if (marl_we) mar_d[7:0]  = d;
            if (marh_we) mar_d[15:8] = d;
        end
    end

    reg_cell #(
        .W(16),
        .RESET_VALUE(16'h0000)
    ) mar_cell (
        .clk(clk),
        .rst_n(rst_n),
        .we(marl_we | marh_we | inc),
        .oe(1'b1),
        .d(mar_d),
        .out(mar_q)
    );

    assign marl_out = mar_q[7:0];
    assign marh_out = mar_q[15:8];

endmodule
