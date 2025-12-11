using System;
using System.Web;
using System.Web.Mvc;
using System.IO;
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

            // Placeholder: file is received but not yet processed or saved.
            // This keeps the workflow complete without affecting existing data.
            TempData["SuccessMessage"] = "File received successfully. Upload processing will be implemented soon.";

            return RedirectToAction("Index");
        }
    }
}
