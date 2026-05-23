module reg_cell #(
    parameter int W = 8,
    parameter logic [W-1:0] RESET_VALUE = '0
)(
    input  logic         clk,
    input  logic         rst_n,
    input  logic         we,
    input  logic         oe,
    input  logic [W-1:0] d,
    output logic [W-1:0] out
);

    logic [W-1:0] reg_q;

    always_ff @(posedge clk or negedge rst_n) begin
        if (!rst_n) reg_q <= RESET_VALUE;
        else if (we) reg_q <= d;
    end

    assign out = oe ? reg_q : '0;

endmodule
