module core_cluster (
    input  logic       clk,
    input  logic       rst_n,
    input  logic [7:0] a,
    input  logic [7:0] b,
    output logic [7:0] result,
    output logic       valid
);
    logic parity_i;

    logic_unit u_logic (
        .a(a),
        .b(b),
        .sum(result),
        .parity(parity_i)
    );

    status_reg u_status (
        .clk(clk),
        .rst_n(rst_n),
        .valid_in(parity_i),
        .valid_out(valid)
    );
endmodule
