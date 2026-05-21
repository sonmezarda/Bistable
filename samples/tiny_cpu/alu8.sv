module alu8 (
    input  logic [7:0] acc,
    input  logic [7:0] data_in,
    input  logic [7:0] immediate,
    input  logic       use_imm,
    input  logic [2:0] alu_op,
    output logic [7:0] result,
    output logic       zero,
    output logic       carry
);
    logic [7:0] rhs;
    logic [8:0] wide_sum;

    assign rhs = use_imm ? immediate : data_in;
    assign wide_sum = {1'b0, acc} + {1'b0, rhs};

    always_comb begin
        unique case (alu_op)
            3'd0: result = rhs;
            3'd1: result = wide_sum[7:0];
            3'd2: result = acc ^ rhs;
            3'd3: result = acc & rhs;
            3'd4: result = 8'h80;
            default: result = acc;
        endcase
    end

    assign zero = result == 8'h00;
    assign carry = wide_sum[8];
endmodule
