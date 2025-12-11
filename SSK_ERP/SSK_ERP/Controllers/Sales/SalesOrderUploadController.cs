using System;
using System.Web;
using System.Web.Mvc;
using System.IO;
using System.Text;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using SSK_ERP.Filters;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    [Authorize(Roles = "SalesOrderCreate")]
    public class SalesOrderUploadController : Controller
    {
        [HttpGet]
        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Index(HttpPostedFileBase file)
        {
            if (file == null || file.ContentLength == 0)
            {
                TempData["ErrorMessage"] = "Please select a file to upload.";
                return View();
            }

            var extension = Path.GetExtension(file.FileName) ?? string.Empty;
            if (!extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Invalid file type. Only PDF files (.pdf) are allowed.";
                return View();
            }

            string extractedText = string.Empty;

            try
            {
                file.InputStream.Position = 0;

                using (var ms = new MemoryStream())
                {
                    file.InputStream.CopyTo(ms);
                    ms.Position = 0;

                    using (var reader = new PdfReader(ms))
                    using (var pdfDoc = new PdfDocument(reader))
                    {
                        var sb = new StringBuilder();
                        int totalPages = pdfDoc.GetNumberOfPages();

                        for (int page = 1; page <= totalPages; page++)
                        {
                            var strategy = new SimpleTextExtractionStrategy();
                            string pageText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(page), strategy);
                            sb.AppendLine(pageText);
                        }

                        extractedText = sb.ToString();
                    }
                }
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] = "There was a problem reading the PDF file.";
                return View();
            }

            ViewBag.ExtractedText = extractedText;
            TempData["SuccessMessage"] = "File uploaded and text extracted successfully.";

            return View();
        }
    }
}
