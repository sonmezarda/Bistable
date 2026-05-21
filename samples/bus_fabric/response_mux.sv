module response_mux (
    input  logic       sel_gpio,
    input  logic       sel_timer,
    input  logic [7:0] gpio_rdata,
    input  logic [7:0] timer_rdata,
    input  logic       gpio_ready,
    input  logic       timer_ready,
    input  logic       timer_irq,
    output logic [7:0] rdata,
    output logic       ready,
    output logic       irq
);
    always_comb begin
        rdata = 8'h00;
        ready = 1'b0;
        if (sel_gpio) begin
            rdata = gpio_rdata;
            ready = gpio_ready;
        end else if (sel_timer) begin
            rdata = timer_rdata;
            ready = timer_ready;
        end
    end

    assign irq = timer_irq;
endmodule
