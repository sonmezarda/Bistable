module status_flags (
    input  logic clk,
    input  logic rst_n,
    input  logic enable,
    input  logic irq,
    input  logic zero_in,
    input  logic carry_in,
    input  logic halt_next,
    output logic irq_pending,
    output logic halted
);
    logic zero_latched;
    logic carry_latched;

    always_ff @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            zero_latched <= 1'b0;
            carry_latched <= 1'b0;
            irq_pending <= 1'b0;
            halted <= 1'b0;
        end else if (enable) begin
            zero_latched <= zero_in;
            carry_latched <= carry_in;
            irq_pending <= irq_pending | irq;
            halted <= halted | halt_next;
        end
    end
endmodule
