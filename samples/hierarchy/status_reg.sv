module status_reg (
    input  logic clk,
    input  logic rst_n,
    input  logic valid_in,
    output logic valid_out
);
    always_ff @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            valid_out <= 1'b0;
        end else begin
            valid_out <= valid_in;
        end
    end
endmodule
