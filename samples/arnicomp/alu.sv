module alu (
    input  logic [7:0] a,
    input  logic [7:0] b,
    input  logic [1:0] ops,
    input  logic       negative,
    input  logic       c_in,
    output logic [7:0] result,
    output logic       zero_flag,
    output logic       negative_flag,
    output logic       carry_flag,
    output logic       overflow_flag
);

    logic [7:0] sum;
    logic       carry_out;
    logic       overflow_out;
    logic [8:0] arith_tmp;
    logic [7:0] add_operand;
    logic [7:0] sub_operand;

    always_comb begin
        sum          = '0;
        carry_out    = 1'b0;
        overflow_out = 1'b0;
        arith_tmp    = '0;
        add_operand  = b + {7'b0, c_in};
        sub_operand  = b + {7'b0, ~c_in};

        case (ops)
            2'b00:
                if (!negative) begin
                    arith_tmp    = {1'b0, a} + {1'b0, b} + {8'b0, c_in};
                    carry_out    = arith_tmp[8];
                    sum          = arith_tmp[7:0];
                    overflow_out = ~(a[7] ^ add_operand[7]) & (a[7] ^ sum[7]);
                end else begin
                    arith_tmp    = {1'b0, a} - {1'b0, b} - {8'b0, ~c_in};
                    carry_out    = ~arith_tmp[8];
                    sum          = arith_tmp[7:0];
                    overflow_out = (a[7] ^ sub_operand[7]) & (a[7] ^ sum[7]);
                end
            2'b01: sum = a & b;
            2'b10: sum = a ^ b;
            2'b11: sum = ~b;
        endcase
    end

    assign result        = sum;
    assign zero_flag     = (sum == 8'h00);
    assign negative_flag = sum[7];
    assign carry_flag    = carry_out;
    assign overflow_flag = overflow_out;

endmodule
