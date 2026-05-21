module timer_peripheral (
    input  logic       clk,
    input  logic       rst_n,
    input  logic       select_i,
    input  logic       write_i,
    input  logic [7:0] wdata,
    output logic [7:0] rdata,
    output logic       ready,
    output logic       irq
);
    logic [7:0] reload_value;
    logic [7:0] counter;

    always_ff @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            reload_value <= 8'h10;
            counter <= 8'h10;
        end else if (select_i && write_i) begin
            reload_value <= wdata;
            counter <= wdata;
        end else if (counter == 8'h00) begin
            counter <= reload_value;
        end else begin
            counter <= counter - 8'h01;
        end
    end

    assign rdata = counter;
    assign ready = select_i;
    assign irq = counter == 8'h00;
endmodule
