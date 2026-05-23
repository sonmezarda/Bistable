// Data bus source mux.
// Source encoding: 000=Ra 001=Rd 010=Rb 011=ACC 100=ZERO 101=LRL 110=LRH 111=M
module bus_selector (
    input  logic [2:0] sel,
    input  logic       out_en,
    input  logic [7:0] a,
    input  logic [7:0] d,
    input  logic [7:0] b,
    input  logic [7:0] acc,
    input  logic [7:0] lrl,
    input  logic [7:0] lrh,
    input  logic [7:0] m,
    output logic [7:0] out
);

    logic [7:0] out_sel;

    always_comb begin
        case (sel)
            3'b000: out_sel = a;
            3'b001: out_sel = d;
            3'b010: out_sel = b;
            3'b011: out_sel = acc;
            3'b100: out_sel = 8'h00;
            3'b101: out_sel = lrl;
            3'b110: out_sel = lrh;
            3'b111: out_sel = m;
            default: out_sel = 8'h00;
        endcase
    end

    assign out = out_en ? out_sel : 8'd0;

endmodule
