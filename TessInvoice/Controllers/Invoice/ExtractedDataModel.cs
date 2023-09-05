namespace TessInvoice.Controllers.Invoice
{
    internal class ExtractedDataModel
    {
        public string? InvoiceNumber { get; internal set; }
        public string? Date { get; internal set; }
        public string? Amount { get; internal set; }
        public string? FileName { get; set; }
    }
}