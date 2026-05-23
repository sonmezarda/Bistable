// RA register — supports SMSBRA instruction which forces MSB high.
module reg_a (
    input  logic       clk,
    input  logic       rst_n,
    input  logic       we,
    input  logic       smsbra,
    input  logic [7:0] d,
    output logic [7:0] out
);

    logic [7:0] data_in;

    always_comb begin
        if (smsbra)
            data_in = out | 8'h80;
        else
            data_in = d;
    end

    reg_cell #(
        .W(8),
        .RESET_VALUE(8'h00)
    ) ra_cell (
        .clk(clk),
        .rst_n(rst_n),
        .we(we | smsbra),
        .oe(1'b1),
        .d(data_in),
        .out(out)
    );

endmodule
