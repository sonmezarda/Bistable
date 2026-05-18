module counter #(
    parameter int W = 8
) (
    input  logic         clk,
    input  logic         rst_n,
    input  logic         enable,
    output logic [W-1:0] count,
    output logic         terminal
);
    always_ff @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            count <= '0;
        end else if (enable) begin
            count <= count + 1'b1;
        end
    end

    assign terminal = &count;
endmodule
