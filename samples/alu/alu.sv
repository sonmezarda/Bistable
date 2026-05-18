module alu #(
    parameter int W = 8
) (
    input  logic         clk,
    input  logic         rst_n,
    input  logic [W-1:0] a,
    input  logic [W-1:0] b,
    input  logic [2:0]   op,
    output logic [W:0]   y,
    output logic         zero
);
    always_comb begin
        unique case (op)
            3'd0: y = a + b;
            3'd1: y = a - b;
            3'd2: y = {1'b0, a} & {1'b0, b};
            3'd3: y = {1'b0, a} | {1'b0, b};
            default: y = '0;
        endcase

        zero = (y == '0);
    end
endmodule
