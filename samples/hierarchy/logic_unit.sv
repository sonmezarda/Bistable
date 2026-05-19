module logic_unit (
    input  logic [7:0] a,
    input  logic [7:0] b,
    output logic [7:0] sum,
    output logic       parity
);
    assign sum = a + b;
    assign parity = ^sum;
endmodule
