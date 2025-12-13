using System;
using System.Web;
using System.Web.Mvc;
using System.IO;
using System.Text;
using System.Linq;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    [Authorize(Roles = "SalesOrderCreate")]
    public class SalesOrderUploadController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int SalesOrderRegisterId = 1;

        private string ConvertAmountToWords(decimal amount)
        {
            try
            {
                string[] ones = { "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine" };
                string[] teens = { "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen" };
                string[] tens = { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };

                if (amount == 0) return "Zero Rupees Only";

                int rupees = (int)amount;
                int paise = (int)((amount - rupees) * 100);

                string words = string.Empty;

                if (rupees >= 10000000)
                {
                    words += ConvertNumberToWords(rupees / 10000000, ones, teens, tens) + " Crore ";
                    rupees %= 10000000;
                }
                if (rupees >= 100000)
                {
                    words += ConvertNumberToWords(rupees / 100000, ones, teens, tens) + " Lakh ";
                    rupees %= 100000;
                }
                if (rupees >= 1000)
                {
                    words += ConvertNumberToWords(rupees / 1000, ones, teens, tens) + " Thousand ";
                    rupees %= 1000;
                }
                if (rupees >= 100)
                {
                    words += ConvertNumberToWords(rupees / 100, ones, teens, tens) + " Hundred ";
                    rupees %= 100;
                }
                if (rupees > 0)
                {
                    words += ConvertNumberToWords(rupees, ones, teens, tens);
                }

                words = words.Trim() + " Rupees";

                if (paise > 0)
                {
                    words += " and " + ConvertNumberToWords(paise, ones, teens, tens) + " Paise";
                }

                return words + " Only";
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ConvertNumberToWords(int number, string[] ones, string[] teens, string[] tens)
        {
            if (number < 10) return ones[number];
            if (number < 20) return teens[number - 10];
            if (number < 100) return tens[number / 10] + (number % 10 > 0 ? " " + ones[number % 10] : string.Empty);
            return string.Empty;
        }

        private void PopulateCustomerList(int? selectedCustomerId = null)
        {
            var customerList = db.CustomerMasters
                .Where(c => c.DISPSTATUS == 0)
                .OrderBy(c => c.CATENAME)
                .Select(c => new
                {
                    c.CATEID,
                    c.CATENAME
                })
                .ToList();

            ViewBag.CustomerList = new SelectList(customerList, "CATEID", "CATENAME", selectedCustomerId);
        }

        [HttpGet]
        public ActionResult Index()
        {
            PopulateCustomerList(null);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Index(HttpPostedFileBase file, int? customerId)
        {
            PopulateCustomerList(customerId);

            if (!customerId.HasValue || customerId.Value <= 0)
            {
                TempData["ErrorMessage"] = "Please select a customer.";
                return View();
            }

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
                var uploadsDir = Server.MapPath("~/Uploads/SalesOrderPdfs");
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }

                var safeName = Path.GetFileNameWithoutExtension(file.FileName);
                var ext = Path.GetExtension(file.FileName) ?? string.Empty;
                var uniqueName = string.Format("{0}_{1:yyyyMMddHHmmssfff}{2}", safeName, DateTime.Now, ext);
                var fullPath = Path.Combine(uploadsDir, uniqueName);

                file.SaveAs(fullPath);

                using (var reader = new PdfReader(fullPath))
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
            catch (Exception)
            {
                TempData["ErrorMessage"] = "There was a problem reading the PDF file.";
                return View();
            }

            // Directly process extracted text into TransactionMaster / TransactionDetail and temp tables,
            // then redirect to SalesOrder index (single-step flow for the user).
            return SaveTemp(extractedText, file.FileName, customerId);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SaveTemp(string extractedText, string originalFileName, int? customerId)
        {
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                TempData["ErrorMessage"] = "No extracted data to save.";
                return RedirectToAction("Index");
            }

            try
            {
                var uploadedBy = (User != null && User.Identity != null && User.Identity.IsAuthenticated)
                    ? User.Identity.Name
                    : "Upload";

                // Unique batch id for this upload so we can safely clean up temp rows later
                var uploadBatchId = Guid.NewGuid();

                string fullText = extractedText ?? string.Empty;
                var allLines = fullText
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .ToList();

                // ---------------- Header parsing ----------------
                string poNumber = null;
                DateTime? poDate = null;
                string billingName = null;
                string billingCustomerName = null;
                string billingAddress = null;
                string billingGstin = null;
                string supplierName = null;
                decimal? totalAmount = null;
                decimal? grossAmount = null;
                int? creditPeriodDays = null;
                DateTime? receiveByDate = null;
                DateTime? approvedDate = null;

                // PO number and PO date
                var poMatch = Regex.Match(fullText,
                    @"PO\s*#\s*(?<po>.+?)\s+PO Date\s+(?<date>\d{1,2}/\d{1,2}/\d{4})",
                    RegexOptions.IgnoreCase);
                if (poMatch.Success)
                {
                    poNumber = poMatch.Groups["po"].Value.Trim();
                    var dateStr = poMatch.Groups["date"].Value.Trim();
                    if (DateTime.TryParse(dateStr, CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.None, out var dtPo))
                    {
                        poDate = dtPo;
                    }
                }

                // Billing and supplier names (multi-line billing block, then supplier name)
                int headerIndex = allLines.FindIndex(l => l.StartsWith("Billing Name and Address", StringComparison.OrdinalIgnoreCase));
                if (headerIndex >= 0)
                {
                    int indiaIndex = allLines.FindIndex(headerIndex + 1, l => string.Equals(l, "India", StringComparison.OrdinalIgnoreCase));

                    if (indiaIndex >= 0)
                    {
                        // Billing block: from first line under the header up to the line after "India"
                        int start = headerIndex + 1;
                        int end = Math.Min(indiaIndex + 1, allLines.Count - 1); // includes GST line after India

                        if (start <= end)
                        {
                            var billingLines = allLines
                                .Skip(start)
                                .Take(end - start + 1)
                                .ToList();

                            // First line is billing name (e.g. PHARMA STORE)
                            if (billingLines.Count > 0)
                            {
                                billingName = billingLines[0];
                            }

                            // Detect GSTIN in last line if present
                            if (billingLines.Count > 1)
                            {
                                var gstCandidate = billingLines[billingLines.Count - 1].Replace(" ", string.Empty);
                                if (Regex.IsMatch(gstCandidate, @"^[0-9]{2}[A-Z0-9]{13}$", RegexOptions.IgnoreCase))
                                {
                                    billingGstin = billingLines[billingLines.Count - 1];
                                    billingLines.RemoveAt(billingLines.Count - 1);
                                }
                            }

                            // Second line contains customer name + maybe part of address
                            if (billingLines.Count > 1)
                            {
                                var secondLine = billingLines[1];
                                int commaIndex = secondLine.IndexOf(',');
                                var addrParts = new List<string>();

                                if (commaIndex > 0)
                                {
                                    billingCustomerName = secondLine.Substring(0, commaIndex).Trim();
                                    var restSecond = secondLine.Substring(commaIndex + 1).Trim();
                                    if (!string.IsNullOrEmpty(restSecond))
                                    {
                                        addrParts.Add(restSecond);
                                    }
                                }
                                else
                                {
                                    billingCustomerName = secondLine.Trim();
                                }

                                // Remaining lines (from index 2 onwards) are address lines
                                for (int i = 2; i < billingLines.Count; i++)
                                {
                                    addrParts.Add(billingLines[i]);
                                }

                                if (addrParts.Count > 0)
                                {
                                    billingAddress = string.Join(Environment.NewLine, addrParts);
                                }
                            }
                        }

                        // Supplier name: first line after the billing block (e.g. 8848 SMA REMEDIES)
                        if (indiaIndex + 2 < allLines.Count)
                        {
                            supplierName = allLines[indiaIndex + 2];
                        }
                    }
                    else if (headerIndex + 1 < allLines.Count)
                    {
                        // Fallback: at least capture the first line under billing header
                        billingName = allLines[headerIndex + 1];
                    }
                }

                // Totals
                var totalAmtMatch = Regex.Match(fullText,
                    @"Total Amount\s+Rs\.\s*(?<amt>[0-9,]+\.\d+)",
                    RegexOptions.IgnoreCase);
                if (totalAmtMatch.Success)
                {
                    var amtStr = totalAmtMatch.Groups["amt"].Value.Replace(",", "");
                    if (decimal.TryParse(amtStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
                    {
                        totalAmount = dec;
                    }
                }

                var grossAmtMatch = Regex.Match(fullText,
                    @"Gross Amount\s+Rs\.\s*(?<amt>[0-9,]+\.\d+)",
                    RegexOptions.IgnoreCase);
                if (grossAmtMatch.Success)
                {
                    var amtStr = grossAmtMatch.Groups["amt"].Value.Replace(",", "");
                    if (decimal.TryParse(amtStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
                    {
                        grossAmount = dec;
                    }
                }

                // Credit period
                var creditMatch = Regex.Match(fullText,
                    @"Credit Period\s+(?<days>\d+)\s+days",
                    RegexOptions.IgnoreCase);
                if (creditMatch.Success && int.TryParse(creditMatch.Groups["days"].Value, out var days))
                {
                    creditPeriodDays = days;
                }

                // Receive By date
                var receiveMatch = Regex.Match(fullText,
                    @"Receive By\s+(?<date>\d{1,2}/\d{1,2}/\d{4})",
                    RegexOptions.IgnoreCase);
                if (receiveMatch.Success)
                {
                    var dateStr = receiveMatch.Groups["date"].Value.Trim();
                    if (DateTime.TryParse(dateStr, CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.None, out var dtRec))
                    {
                        receiveByDate = dtRec;
                    }
                }

                // Approved Date (full date-time string before "Total CGST Amt")
                var approvedMatch = Regex.Match(fullText,
                    @"Approved Date\s+(?<dt>.+?)\s+Total CGST Amt",
                    RegexOptions.IgnoreCase);
                if (approvedMatch.Success)
                {
                    var dtStr = approvedMatch.Groups["dt"].Value.Trim();
                    if (DateTime.TryParse(dtStr, CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.AssumeLocal, out var dtApp))
                    {
                        approvedDate = dtApp;
                    }
                }

                TransactionMaster createdMaster = null;

                if (customerId.HasValue && customerId.Value > 0)
                {
                    var compyObj = Session["CompyId"] ?? Session["compyid"];
                    int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                    string userName = User != null && User.Identity != null && User.Identity.IsAuthenticated
                        ? User.Identity.Name
                        : "System";

                    short tranStateType = 0;
                    var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == customerId.Value);
                    if (customer != null)
                    {
                        var state = db.StateMasters.FirstOrDefault(s => s.STATEID == customer.STATEID);
                        if (state != null)
                        {
                            tranStateType = state.STATETYPE;
                        }
                    }

                    var maxTranNo = db.TransactionMasters
                        .Where(t => t.COMPYID == compyId && t.REGSTRID == SalesOrderRegisterId)
                        .Select(t => (int?)t.TRANNO)
                        .Max();

                    int nextTranNo = (maxTranNo ?? 0) + 1;

                    decimal gross = grossAmount ?? 0m;
                    decimal net = totalAmount ?? gross;

                    var master = new TransactionMaster
                    {
                        COMPYID = compyId,
                        SDPTID = 0,
                        REGSTRID = SalesOrderRegisterId,
                        TRANBTYPE = 0,
                        TRANDATE = DateTime.Today,
                        TRANTIME = DateTime.Now,
                        TRANNO = nextTranNo,
                        TRANDNO = nextTranNo.ToString("D4"),
                        TRANREFID = customerId.Value,
                        TRANREFNAME = customer != null ? customer.CATENAME : (billingCustomerName ?? billingName ?? string.Empty),
                        TRANSTATETYPE = tranStateType,
                        TRANREFNO = "-",
                        TRANGAMT = 0m,
                        TRANCGSTAMT = 0m,
                        TRANSGSTAMT = 0m,
                        TRANIGSTAMT = 0m,
                        TRANNAMT = 0m,
                        TRANAMTWRDS = null,
                        TRANLMID = 0,
                        TRANPCOUNT = 0,
                        TRANNARTN = null,
                        TRANRMKS = null,
                        EXPRTSTATUS = 0,
                        CUSRID = userName,
                        LMUSRID = userName,
                        DISPSTATUS = 0,
                        PRCSDATE = DateTime.Now
                    };

                    db.TransactionMasters.Add(master);
                    db.SaveChanges();

                    createdMaster = master;
                }

                // Insert structured header row (plus full text)
                object DbValue(object value) => value ?? (object)DBNull.Value;

                int masterId = db.Database.SqlQuery<int>(
                    "INSERT INTO TransactionMasterTemp (UploadBatchId, OriginalPdfFileName, UploadedOn, UploadedBy, PoNumber, PoDate, BillingName, BillingCustomerName, BillingAddress, BillingGstin, SupplierName, TotalAmount, GrossAmount, CreditPeriodDays, ReceiveByDate, ApprovedDate, FullExtractedText) " +
                    "VALUES (@p0, @p1, GETDATE(), @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15); " +
                    "SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    uploadBatchId,
                    originalFileName ?? string.Empty,
                    uploadedBy,
                    DbValue(poNumber),
                    DbValue(poDate),
                    DbValue(billingName),
                    DbValue(billingCustomerName),
                    DbValue(billingAddress),
                    DbValue(billingGstin),
                    DbValue(supplierName),
                    DbValue(totalAmount),
                    DbValue(grossAmount),
                    DbValue(creditPeriodDays),
                    DbValue(receiveByDate),
                    DbValue(approvedDate),
                    fullText
                ).Single();

                // ---------------- Detail parsing (only item rows) ----------------
                int itemsHeaderIndex = allLines.FindIndex(l => l.StartsWith("Sno Item/Drug Name", StringComparison.OrdinalIgnoreCase));
                if (itemsHeaderIndex >= 0)
                {
                    var itemBlocks = new List<string>();
                    StringBuilder currentItem = null;

                    for (int i = itemsHeaderIndex + 1; i < allLines.Count; i++)
                    {
                        var line = allLines[i];

                        if (line.StartsWith("Prepared by", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }

                        bool startsWithNumber = Regex.IsMatch(line, @"^\d+(\.\d+)?\s+");

                        if (startsWithNumber)
                        {
                            if (currentItem != null)
                            {
                                itemBlocks.Add(currentItem.ToString().Trim());
                            }
                            currentItem = new StringBuilder();
                            currentItem.Append(line);
                        }
                        else if (currentItem != null)
                        {
                            currentItem.Append(" " + line);
                        }
                    }

                    if (currentItem != null)
                    {
                        itemBlocks.Add(currentItem.ToString().Trim());
                    }

                    int lineNo = 1;

                    foreach (var block in itemBlocks)
                    {
                        var normalized = Regex.Replace(block, @"\s+", " ").Trim();
                        var tokens = normalized.Split(' ');
                        if (tokens.Length < 10)
                        {
                            continue;
                        }

                        int hsnIndex = Array.FindIndex(tokens, t => Regex.IsMatch(t, @"^\d{6,8}$"));
                        if (hsnIndex <= 1)
                        {
                            continue;
                        }

                        string itemName = string.Join(" ", tokens.Skip(1).Take(hsnIndex - 1));
                        string hsnCode = tokens[hsnIndex];

                        decimal ParseDecimalOrZero(string s)
                        {
                            if (string.IsNullOrWhiteSpace(s)) return 0m;
                            s = s.Replace(",", string.Empty);
                            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                            {
                                return val;
                            }
                            return 0m;
                        }

                        decimal qty = 0m;
                        if (hsnIndex + 1 < tokens.Length)
                        {
                            qty = ParseDecimalOrZero(tokens[hsnIndex + 1]);
                        }

                        int idx = hsnIndex + 2; // skip qty
                        if (idx < tokens.Length)
                        {
                            idx++; // skip Free Qty flag (e.g. "No")
                        }

                        var uqcParts = new List<string>();
                        while (idx < tokens.Length && tokens[idx] != "Rs.")
                        {
                            uqcParts.Add(tokens[idx]);
                            idx++;
                        }
                        string uqc = string.Join(" ", uqcParts).Trim();

                        decimal ratePerUnit = 0m;
                        decimal discountPercent = 0m;
                        decimal cgstPercent = 0m;
                        decimal sgstPercent = 0m;
                        decimal igstPercent = 0m;
                        decimal grossLineAmount = 0m;

                        if (idx < tokens.Length && tokens[idx] == "Rs." && idx + 1 < tokens.Length)
                        {
                            ratePerUnit = ParseDecimalOrZero(tokens[idx + 1]);
                            idx += 2;

                            if (idx < tokens.Length) { discountPercent = ParseDecimalOrZero(tokens[idx]); idx++; }
                            if (idx < tokens.Length) { cgstPercent = ParseDecimalOrZero(tokens[idx]); idx++; }
                            if (idx < tokens.Length) { sgstPercent = ParseDecimalOrZero(tokens[idx]); idx++; }
                            if (idx < tokens.Length) { igstPercent = ParseDecimalOrZero(tokens[idx]); idx++; }

                            int secondRsIndex = Array.FindIndex(tokens, idx, t => t == "Rs.");
                            if (secondRsIndex >= 0 && secondRsIndex + 1 < tokens.Length)
                            {
                                grossLineAmount = ParseDecimalOrZero(tokens[secondRsIndex + 1]);
                            }
                            else
                            {
                                grossLineAmount = ParseDecimalOrZero(tokens.Last());
                            }
                        }

                        var rawLineText = block.Length > 500 ? block.Substring(0, 500) : block;

                        db.Database.ExecuteSqlCommand(
                            "INSERT INTO TransactionDetailTemp (TransactionMasterTempId, [LineNo], ItemDrugName, HsnCode, Qty, FreeQty, Uqc, RatePerUnit, DiscountPercent, CgstPercent, SgstPercent, IgstPercent, GrossAmount, RawLineText) " +
                            "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13);",
                            masterId,
                            lineNo,
                            itemName,
                            hsnCode,
                            qty,
                            0m, // FreeQty
                            uqc,
                            ratePerUnit,
                            discountPercent,
                            cgstPercent,
                            sgstPercent,
                            igstPercent,
                            grossLineAmount,
                            rawLineText
                        );

                        lineNo++;
                    }
                }

                if (createdMaster != null)
                {
                    db.Database.ExecuteSqlCommand(
                        "EXEC PR_TRANSACTIONDETAILPDF_INSERT @Tranmid = @p0",
                        createdMaster.TRANMID);

                    var totals = db.TransactionDetails
                        .Where(d => d.TRANMID == createdMaster.TRANMID)
                        .GroupBy(d => d.TRANMID)
                        .Select(g => new
                        {
                            Gross = g.Sum(x => (decimal?)x.TRANDGAMT) ?? 0m,
                            Cgst = g.Sum(x => (decimal?)x.TRANDCGSTAMT) ?? 0m,
                            Sgst = g.Sum(x => (decimal?)x.TRANDSGSTAMT) ?? 0m,
                            Igst = g.Sum(x => (decimal?)x.TRANDIGSTAMT) ?? 0m,
                            Net = g.Sum(x => (decimal?)x.TRANDNAMT) ?? 0m
                        })
                        .FirstOrDefault();

                    if (totals != null)
                    {
                        createdMaster.TRANGAMT = totals.Gross;
                        createdMaster.TRANCGSTAMT = totals.Cgst;
                        createdMaster.TRANSGSTAMT = totals.Sgst;
                        createdMaster.TRANIGSTAMT = totals.Igst;
                        createdMaster.TRANNAMT = totals.Net;
                        createdMaster.TRANAMTWRDS = ConvertAmountToWords(totals.Net);

                        db.SaveChanges();
                    }

                    db.Database.ExecuteSqlCommand(
                        "DELETE FROM TransactionDetailTemp WHERE TransactionMasterTempId = @p0; DELETE FROM TransactionMasterTemp WHERE UploadBatchId = @p1;",
                        masterId,
                        uploadBatchId);
                }

                TempData["SuccessMessage"] = "Extracted data saved to temporary tables successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error saving extracted data: " + ex.Message;
            }

            return RedirectToAction("Index", "SalesOrder");
        }
    }
}
