module gpio_peripheral (
    input  logic       clk,
    input  logic       rst_n,
    input  logic       select_i,
    input  logic       write_i,
    input  logic [7:0] wdata,
    output logic [7:0] rdata,
    output logic       ready
);
    logic [7:0] gpio_reg;

    always_ff @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            gpio_reg <= 8'h00;
        end else if (select_i && write_i) begin
            gpio_reg <= wdata;
        end
    end

    assign rdata = gpio_reg;
    assign ready = select_i;
endmodule
