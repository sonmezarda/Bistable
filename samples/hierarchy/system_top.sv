module system_top (
    input  logic       clk,
    input  logic       rst_n,
    input  logic [7:0] a,
    input  logic [7:0] b,
    output logic [7:0] result,
    output logic       valid
);
    core_cluster u_core (
        .clk(clk),
        .rst_n(rst_n),
        .a(a),
        .b(b),
        .result(result),
        .valid(valid)
    );
endmodule
