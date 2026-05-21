module fabric_top (
    input  logic       clk,
    input  logic       rst_n,
    input  logic       read,
    input  logic       write,
    input  logic [7:0] addr,
    input  logic [7:0] wdata,
    output logic [7:0] rdata,
    output logic       ready,
    output logic       irq
);
    logic sel_gpio;
    logic sel_timer;
    logic gpio_ready;
    logic timer_ready;
    logic [7:0] gpio_rdata;
    logic [7:0] timer_rdata;
    logic timer_irq;

    address_decoder u_decode (
        .addr(addr),
        .read(read),
        .write(write),
        .sel_gpio(sel_gpio),
        .sel_timer(sel_timer)
    );

    gpio_peripheral u_gpio (
        .clk(clk),
        .rst_n(rst_n),
        .select_i(sel_gpio),
        .write_i(write),
        .wdata(wdata),
        .rdata(gpio_rdata),
        .ready(gpio_ready)
    );

    timer_peripheral u_timer (
        .clk(clk),
        .rst_n(rst_n),
        .select_i(sel_timer),
        .write_i(write),
        .wdata(wdata),
        .rdata(timer_rdata),
        .ready(timer_ready),
        .irq(timer_irq)
    );

    response_mux u_response (
        .sel_gpio(sel_gpio),
        .sel_timer(sel_timer),
        .gpio_rdata(gpio_rdata),
        .timer_rdata(timer_rdata),
        .gpio_ready(gpio_ready),
        .timer_ready(timer_ready),
        .timer_irq(timer_irq),
        .rdata(rdata),
        .ready(ready),
        .irq(irq)
    );
endmodule
