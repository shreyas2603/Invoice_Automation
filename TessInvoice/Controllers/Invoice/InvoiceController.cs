using System.Text;
using System.Text.RegularExpressions;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Mvc;
using Tesseract;
using System.Drawing;
using iTextSharp.text.pdf.parser;
using System.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using TessInvoice.Models.ViewModels;
using System.IO;

namespace TessInvoice.Controllers
{
    public class InvoiceController : Controller
    {

        [HttpGet]
        //[Authorize]

        public IActionResult ExtractData()
        {
            return View();
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login()
        {
            return View();
        }

        private readonly string _uploadFolderPath;

        public InvoiceController()
        {
            // Set the path to the "Invoice" folder within the project
            _uploadFolderPath = @"C:\Users\shreyas\source\repos\TessInvoice\TessInvoice\wwwroot\Invoices";
        }


        [HttpGet]
        [HttpPost]
        public IActionResult Authenticate(LoginViewModel model)
        {
            string connectionString = "Server=LAPTOP-D6CUE1L1;Database=Invoice;Integrated Security=true;";
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string selectQuery = "SELECT COUNT(*) FROM Login2 WHERE Username = @Username AND Password = @Password";

                using (SqlCommand command = new SqlCommand(selectQuery, connection))
                {
                    command.Parameters.AddWithValue("@Username", model.Username);
                    command.Parameters.AddWithValue("@Password", model.Password);

                    int count = (int)command.ExecuteScalar();
                    if (count > 0)
                    {
                        // Valid credentials, redirect to ExtractData action
                        return View("ExtractData");
                    }
                    else
                    {
                        // Invalid credentials, show login view with error message
                        ViewBag.ErrorMessage = "Invalid username or password.";
                        return View("Login");
                    }
                }
            }
        }


        [HttpGet]
        public IActionResult ViewInvoiceData()
        {
            List<ExtractedDataModel> invoiceData = GetInvoiceDataFromDatabase();
            return View(invoiceData);
        }



        private List<ExtractedDataModel> GetInvoiceDataFromDatabase()
        {
            List<ExtractedDataModel> invoiceData = new List<ExtractedDataModel>();

            // Implement the database query logic here to fetch data from the InvoiceInfo2 table
            string connectionString = "Server=LAPTOP-D6CUE1L1;Database=Invoice;Integrated Security=true;"; // Update connection string

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string selectQuery = "SELECT InvoiceNumber, Date, Amount, FileName FROM InvoiceInfo2"; // Include FileName in the query

                using (SqlCommand command = new SqlCommand(selectQuery, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ExtractedDataModel data = new ExtractedDataModel
                            {
                                InvoiceNumber = reader["InvoiceNumber"].ToString(),
                                Date = reader["Date"].ToString(),
                                Amount = reader["Amount"].ToString(),
                                FileName = reader["FileName"].ToString() // Set the FileName property
                            };

                            invoiceData.Add(data);
                        }
                    }
                }

                connection.Close();
            }

            return invoiceData;
        }




        [HttpPost]
        public IActionResult ExtractData(IFormFile pdfFile)
        {
            if (pdfFile != null && pdfFile.Length > 0)
            {
                try
                {
                    // Save the uploaded file to the "Invoice" folder
                    var filePath = System.IO.Path.Combine(_uploadFolderPath, pdfFile.FileName);
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        pdfFile.CopyTo(fileStream);
                    }

                    // Extract data from the uploaded PDF
                    string text = ExtractTextFromPdf(pdfFile.OpenReadStream());
                    var extractedData = ExtractInvoiceInfo(text);

                    extractedData.FileName = pdfFile.FileName; // Set the FileName property

                    // Insert extracted data into the database
                    InsertInvoiceDataIntoDatabase(extractedData);

                    ViewBag.ExtractedData = extractedData;

                    return View("ExtractData");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception during text extraction or OCR:");
                    Console.WriteLine(ex);
                }
            }

            return RedirectToAction("ExtractData");
        }


        private static string ExtractTextFromPdf(Stream pdfStream)
        {
            using PdfReader reader = new PdfReader(pdfStream);
            StringBuilder text = new();

            for (int i = 1; i <= reader.NumberOfPages; i++)
            {
                // Render PDF page as image
                var renderedImage = RenderPdfPageAsImage(reader, i);

                // Perform OCR on the rendered image
                string ocrText = PerformOcr(renderedImage);

                text.AppendLine(ocrText);
            }

            return text.ToString();
        }

        [HttpGet]


        private IActionResult InsertInvoiceDataIntoDatabase(ExtractedDataModel extractedData)
        {
            string connectionString = "Server=LAPTOP-D6CUE1L1;Database=Invoice;Integrated Security=true;"; // Update connection string

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Check if the invoice number already exists in the database
                string checkInvoiceNumberQuery = "SELECT COUNT(*) FROM InvoiceInfo2 WHERE InvoiceNumber = @InvoiceNumber";

                using (SqlCommand checkCommand = new SqlCommand(checkInvoiceNumberQuery, connection))
                {
                    checkCommand.Parameters.AddWithValue("@InvoiceNumber", extractedData.InvoiceNumber);

                    int existingCount = (int)checkCommand.ExecuteScalar();

                    if (existingCount > 0)
                    {
                        // Invoice number already exists, return the extracted data
                        ViewBag.ExtractedData = extractedData;
                        return View("ExtractData");
                    }
                }

                // Invoice number doesn't exist, proceed with insertion
                string insertQuery = "INSERT INTO InvoiceInfo2 (InvoiceNumber, Date, Amount, FileName) VALUES (@InvoiceNumber, @Date, @Amount, @FileName)";

                using (SqlCommand command = new SqlCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@InvoiceNumber", extractedData.InvoiceNumber);
                    command.Parameters.AddWithValue("@Date", extractedData.Date);

                    if (decimal.TryParse(extractedData.Amount, out decimal amount))
                    {
                        command.Parameters.AddWithValue("@Amount", amount);
                    }
                    else
                    {
                        Console.WriteLine("Error converting Amount to a numeric value.");
                        command.Parameters.AddWithValue("@Amount", DBNull.Value);
                    }

                    command.Parameters.AddWithValue("@FileName", extractedData.FileName); // Set the FileName parameter

                    command.ExecuteNonQuery();
                }

                connection.Close();
            }

            // Invoice data inserted successfully, return the extracted data
            ViewBag.ExtractedData = extractedData;
            return View("ExtractData");
        }



        private static System.Drawing.Image RenderPdfPageAsImage(PdfReader reader, int pageNumber)
        {
            PdfDictionary pageDict = reader.GetPageN(pageNumber);
            PdfDictionary resources = pageDict.GetAsDict(PdfName.RESOURCES);
            PdfDictionary xObject = resources.GetAsDict(PdfName.XOBJECT);

            foreach (var name in xObject.Keys)
            {
                PdfStream stream = xObject.GetAsStream(name);
                PdfImageObject image = new PdfImageObject((PRStream)stream);
                System.Drawing.Image img = image.GetDrawingImage();
                return img;
            }

            return null;
        }

        private static string PerformOcr(System.Drawing.Image image)
        {
            try
            {
                string tesseractBasePath = "C:\\Program Files\\Tesseract-OCR";
                string tessDataPath = System.IO.Path.Combine(tesseractBasePath, "tessdata");

                using (var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default))
                {
                    using (var pix = PixConverter.ToPix((Bitmap)image))
                    {
                        using (var page = engine.Process(pix))
                        {
                            var result = page.GetText();
                            return result;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error during OCR:");
                Console.WriteLine(ex);
                return string.Empty;
            }
        }

        private ExtractedDataModel ExtractInvoiceInfo(string extractedText)
        {
            ExtractedDataModel extractedData = new ExtractedDataModel();

            string invoiceNumberPattern = @"Tax Invoice No\s*:\s*([^:\r\n]+)";
            string datePattern = @"Date:\s*(\d{2}-\w{3}-\d{4})";
            string amountPattern = @"INVOICE AMOUNT \(INR\) (\d{1,3}(?:,\d{3})*(?:\.\d{2}))";

            Match invoiceNumberMatch = Regex.Match(extractedText, invoiceNumberPattern);
            if (invoiceNumberMatch.Success)
            {
                extractedData.InvoiceNumber = invoiceNumberMatch.Groups[1].Value.Trim();
            }
            else
            {
                string invoiceNumberPattern2 = @"Invoice No\.\s*: ([^:\r\n]+)";
                Match invoiceNumberMatch2 = Regex.Match(extractedText, invoiceNumberPattern2);
                if (invoiceNumberMatch2.Success)
                {
                    extractedData.InvoiceNumber = invoiceNumberMatch2.Groups[1].Value.Trim();
                }
                else
                {
                    string invoiceNumberPattern3 = @"Invoice No:\s*([^\s]+)";
                    Match invoiceNumberMatch3 = Regex.Match(extractedText, invoiceNumberPattern3);
                    if (invoiceNumberMatch3.Success)
                    {
                        extractedData.InvoiceNumber = invoiceNumberMatch3.Groups[1].Value.Trim();
                    }
                    else
                    {
                        string invoiceNumberPattern4 = @"Invoice NO\.\s+([0-9-]+)";
                        Match invoiceNumberMatch4 = Regex.Match(extractedText, invoiceNumberPattern4);
                        if (invoiceNumberMatch4.Success)
                        {
                            extractedData.InvoiceNumber = invoiceNumberMatch4.Groups[1].Value.Trim();
                        }
                    }
                }
            }

            Match dateMatch = Regex.Match(extractedText, datePattern);
            if (dateMatch.Success)
            {
                extractedData.Date = dateMatch.Groups[1].Value.Trim();
            }
            else
            {
                string datePattern2 = @"Date \+ (\d{2}-\d{2}-\d{4})";
                Match dateMatch2 = Regex.Match(extractedText, datePattern2);
                if (dateMatch2.Success)
                {
                    extractedData.Date = dateMatch2.Groups[1].Value.Trim();
                }
                else
                {
                    string datePattern3 = @"Date:\s+(\d{2}\s+\w{3}\s+\d{4})";
                    Match dateMatch3 = Regex.Match(extractedText, datePattern3);
                    if (dateMatch3.Success)
                    {
                        extractedData.Date = dateMatch3.Groups[1].Value.Trim();
                    }
                    else
                    {
                        string datePattern4 = @"Date:\s+(\d{2}/\d{2}/\d{4})";
                        Match dateMatch4 = Regex.Match(extractedText, datePattern4);
                        if (dateMatch4.Success)
                        {
                            extractedData.Date = dateMatch4.Groups[1].Value.Trim();
                        }
                    }

                }
            }

            Match amountMatch = Regex.Match(extractedText, amountPattern);
            if (amountMatch.Success)
            {
                extractedData.Amount = amountMatch.Groups[1].Value.Trim();
            }
            else
            {
                string amountPattern2 = @"Total Invoice Amount \(in Words\)[\s\S]*?(\d{1,3}(?:,\d{3})*(?:\.\d{2}))";
                Match amountMatch2 = Regex.Match(extractedText, amountPattern2);
                if (amountMatch2.Success)
                {
                    extractedData.Amount = amountMatch2.Groups[1].Value.Trim();
                }
                else
                {
                    string amountPattern3 = @"Total\s+(\d+)\s+INR";
                    Match amountMatch3 = Regex.Match(extractedText, amountPattern3);
                    if (amountMatch3.Success)
                    {
                        extractedData.Amount = amountMatch3.Groups[1].Value.Trim();
                    }
                    else
                    {
                        string amountPattern4 = @"Total amount\s+\$([\d,]+\.\d{2})";
                        Match amountMatch4 = Regex.Match(extractedText, amountPattern4);
                        if (amountMatch4.Success)
                        {
                            extractedData.Amount = amountMatch4.Groups[1].Value.Trim();
                        }
                    }

                }
            }

            return extractedData;
        }


    }

    public class ExtractedDataModel
    {

        public string? InvoiceNumber { get; set; }
        public string? Date { get; set; }
        public string? Amount { get; set; }
        public string? FileName { get; set; }
    }
}