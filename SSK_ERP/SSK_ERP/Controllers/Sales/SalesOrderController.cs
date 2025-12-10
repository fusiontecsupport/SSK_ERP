using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Newtonsoft.Json;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class SalesOrderController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();
        private const int SalesOrderRegisterId = 1;

        [Authorize(Roles = "SalesOrderIndex")]
        public ActionResult Index()
        {
            return View();
        }

        [Authorize(Roles = "SalesOrderCreate,SalesOrderEdit")]
        public ActionResult Form(int? id)
        {
            TransactionMaster model;
            var detailRows = new List<SalesOrderDetailRow>();

            if (id.HasValue && id.Value > 0)
            {
                if (!User.IsInRole("SalesOrderEdit"))
                {
                    TempData["ErrorMessage"] = "You do not have permission to edit Sales Orders.";
                    return RedirectToAction("Index");
                }

                model = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id.Value && t.REGSTRID == SalesOrderRegisterId);
                if (model == null)
                {
                    TempData["ErrorMessage"] = "Sales Order not found.";
                    return RedirectToAction("Index");
                }

                var details = db.TransactionDetails.Where(d => d.TRANMID == model.TRANMID).ToList();
                foreach (var d in details)
                {
                    detailRows.Add(new SalesOrderDetailRow
                    {
                        MaterialId = d.TRANDREFID,
                        Qty = d.TRANDQTY,
                        Rate = d.TRANDRATE,
                        Amount = d.TRANDGAMT,
                        ProfitPercent = d.TRANDMTRLPRFT,
                        ActualRate = d.TRANDARATE
                    });
                }
            }
            else
            {
                if (!User.IsInRole("SalesOrderCreate"))
                {
                    TempData["ErrorMessage"] = "You do not have permission to create Sales Orders.";
                    return RedirectToAction("Index");
                }

                model = new TransactionMaster
                {
                    TRANDATE = DateTime.Today,
                    TRANTIME = DateTime.Now,
                    DISPSTATUS = 0
                };

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                var maxTranNo = db.TransactionMasters
                    .Where(t => t.COMPYID == compyId && t.REGSTRID == SalesOrderRegisterId)
                    .Select(t => (int?)t.TRANNO)
                    .Max();

                int nextTranNo = (maxTranNo ?? 0) + 1;
                model.TRANNO = nextTranNo;
                model.TRANDNO = nextTranNo.ToString("D4");
            }

            ViewBag.StatusList = new SelectList(
                new[]
                {
                    new { Value = "0", Text = "Enabled" },
                    new { Value = "1", Text = "Disabled" }
                },
                "Value",
                "Text",
                model.DISPSTATUS.ToString()
            );

            ViewBag.DetailRowsJson = detailRows.Any()
                ? JsonConvert.SerializeObject(detailRows)
                : "[]";

            var customerList = db.CustomerMasters
                .Where(c => c.DISPSTATUS == 0)
                .OrderBy(c => c.CATENAME)
                .Select(c => new
                {
                    c.CATEID,
                    c.CATENAME
                })
                .ToList();

            ViewBag.CustomerList = new SelectList(customerList, "CATEID", "CATENAME", model.TRANREFID);

            ViewBag.StateTypeList = new SelectList(
                new[]
                {
                    new { Value = "0", Text = "Local" },
                    new { Value = "1", Text = "Interstate" }
                },
                "Value",
                "Text",
                model.TRANSTATETYPE.ToString()
            );

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SalesOrderCreate,SalesOrderEdit")]
        public ActionResult savedata(TransactionMaster master, string detailRowsJson)
        {
            try
            {
                bool isEdit = master.TRANMID > 0 &&
                              db.TransactionMasters.Any(t => t.TRANMID == master.TRANMID && t.REGSTRID == SalesOrderRegisterId);

                if (isEdit)
                {
                    if (!User.IsInRole("SalesOrderEdit"))
                    {
                        TempData["ErrorMessage"] = "You do not have permission to edit Sales Orders.";
                        return RedirectToAction("Index");
                    }
                }
                else
                {
                    if (!User.IsInRole("SalesOrderCreate"))
                    {
                        TempData["ErrorMessage"] = "You do not have permission to create Sales Orders.";
                        return RedirectToAction("Index");
                    }
                }

                var details = string.IsNullOrWhiteSpace(detailRowsJson)
                    ? new List<SalesOrderDetailRow>()
                    : JsonConvert.DeserializeObject<List<SalesOrderDetailRow>>(detailRowsJson) ?? new List<SalesOrderDetailRow>();

                details = details
                    .Where(d => d != null && d.MaterialId > 0 && d.Qty > 0 && d.Rate >= 0)
                    .ToList();

                if (!details.Any())
                {
                    TempData["ErrorMessage"] = "Please add at least one detail row.";
                    return RedirectToAction("Form", new { id = isEdit ? (int?)master.TRANMID : null });
                }

                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;
                string userName = User != null && User.Identity != null && User.Identity.IsAuthenticated
                    ? User.Identity.Name
                    : "System";
                if (master.TRANREFID <= 0)
                {
                    TempData["ErrorMessage"] = "Please select a customer.";
                    return RedirectToAction("Form", new { id = isEdit ? (int?)master.TRANMID : null });
                }

                short tranStateType = 0;
                var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == master.TRANREFID);
                if (customer != null)
                {
                    master.TRANREFID = customer.CATEID;
                    master.TRANREFNAME = customer.CATENAME;

                    var state = db.StateMasters.FirstOrDefault(s => s.STATEID == customer.STATEID);
                    if (state != null)
                    {
                        tranStateType = state.STATETYPE;
                    }
                }

                master.TRANSTATETYPE = tranStateType;
                master.COMPYID = compyId;
                master.SDPTID = 0;
                master.REGSTRID = SalesOrderRegisterId;
                master.TRANBTYPE = 0;
                master.EXPRTSTATUS = 0;
                master.TRANTIME = DateTime.Now;
                if (string.IsNullOrWhiteSpace(master.TRANREFNO))
                {
                    master.TRANREFNO = "-";
                }

                if (isEdit)
                {
                    var existing = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == master.TRANMID && t.REGSTRID == SalesOrderRegisterId);
                    if (existing == null)
                    {
                        TempData["ErrorMessage"] = "Sales Order not found.";
                        return RedirectToAction("Index");
                    }

                    existing.TRANDATE = master.TRANDATE;
                    existing.TRANTIME = master.TRANTIME;
                    existing.TRANREFID = master.TRANREFID;
                    existing.TRANREFNAME = master.TRANREFNAME;
                    existing.TRANSTATETYPE = master.TRANSTATETYPE;
                    existing.TRANREFNO = master.TRANREFNO;
                    existing.TRANRMKS = master.TRANRMKS;
                    existing.DISPSTATUS = master.DISPSTATUS;
                    existing.LMUSRID = userName;
                    existing.PRCSDATE = DateTime.Now;

                    var existingDetails = db.TransactionDetails.Where(d => d.TRANMID == existing.TRANMID).ToList();
                    if (existingDetails.Any())
                    {
                        db.TransactionDetails.RemoveRange(existingDetails);
                    }

                    InsertDetails(existing, details);
                    db.SaveChanges();
                }
                else
                {
                    var maxTranNo = db.TransactionMasters
                        .Where(t => t.COMPYID == compyId && t.REGSTRID == SalesOrderRegisterId)
                        .Select(t => (int?)t.TRANNO)
                        .Max();

                    int nextTranNo = (maxTranNo ?? 0) + 1;
                    master.TRANNO = nextTranNo;
                    if (string.IsNullOrWhiteSpace(master.TRANDNO))
                    {
                        master.TRANDNO = nextTranNo.ToString("D4");
                    }

                    master.CUSRID = userName;
                    master.LMUSRID = userName;
                    master.PRCSDATE = DateTime.Now;

                    db.TransactionMasters.Add(master);
                    db.SaveChanges();

                    InsertDetails(master, details);
                    db.SaveChanges();
                }

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        public JsonResult GetAjaxData(DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                var compyObj = Session["CompyId"] ?? Session["compyid"];
                int compyId = compyObj != null ? Convert.ToInt32(compyObj) : 1;

                var query = db.TransactionMasters.Where(t => t.REGSTRID == SalesOrderRegisterId && t.COMPYID == compyId);

                if (fromDate.HasValue)
                {
                    var fd = fromDate.Value.Date;
                    query = query.Where(t => t.TRANDATE >= fd);
                }

                if (toDate.HasValue)
                {
                    var td = toDate.Value.Date;
                    query = query.Where(t => t.TRANDATE <= td);
                }

                var data = query
                    .OrderByDescending(t => t.TRANDATE)
                    .ThenByDescending(t => t.TRANMID)
                    .Select(t => new
                    {
                        t.TRANMID,
                        t.TRANDATE,
                        t.TRANNO,
                        CustomerName = t.TRANREFNAME,
                        Amount = t.TRANNAMT,
                        Status = t.DISPSTATUS == 0 ? "Enabled" : "Disabled"
                    })
                    .ToList();

                return Json(new { data }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { data = new object[0], error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Returns customer address and state details for the selected customer,
        /// including STATETYPE (0 = Local, 1 = Interstate) for use in TRANSTATETYPE.
        /// </summary>
        [HttpGet]
        public JsonResult GetCustomerDetails(int id)
        {
            try
            {
                var customer = db.CustomerMasters.FirstOrDefault(c => c.CATEID == id && c.DISPSTATUS == 0);
                if (customer == null)
                {
                    return Json(new { success = false, message = "Customer not found." }, JsonRequestBehavior.AllowGet);
                }

                var location = db.LocationMasters.FirstOrDefault(l => l.LOCTID == customer.LOCTID);
                var state = db.StateMasters.FirstOrDefault(s => s.STATEID == customer.STATEID);

                var data = new
                {
                    Id = customer.CATEID,
                    Name = customer.CATENAME,
                    Address1 = customer.CATEADDR1,
                    Address2 = customer.CATEADDR2,
                    Address3 = customer.CATEADDR3,
                    Address4 = customer.CATEADDR4,
                    Pincode = customer.CATEADDR5,
                    City = location != null ? location.LOCTDESC : string.Empty,
                    State = state != null ? state.STATEDESC : string.Empty,
                    StateCode = state != null ? state.STATECODE : string.Empty,
                    StateType = state != null ? state.STATETYPE : (short)0,
                    Country = "India"
                };

                return Json(new { success = true, data }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public ActionResult Del(int id)
        {
            try
            {
                if (!User.IsInRole("SalesOrderDelete"))
                {
                    return Json("Access Denied: You do not have permission to delete records. Please contact your administrator.");
                }

                var existing = db.TransactionMasters.FirstOrDefault(t => t.TRANMID == id && t.REGSTRID == SalesOrderRegisterId);
                if (existing == null)
                {
                    return Json("Record not found");
                }

                var details = db.TransactionDetails.Where(d => d.TRANMID == existing.TRANMID).ToList();
                if (details.Any())
                {
                    db.TransactionDetails.RemoveRange(details);
                }

                db.TransactionMasters.Remove(existing);
                db.SaveChanges();

                return Json("Successfully deleted");
            }
            catch (Exception ex)
            {
                return Json("Error: " + ex.Message);
            }
        }

        /// <summary>
        /// Returns material and material group data for the Sales Order Customer Order Details grid.
        /// Used by the SalesOrder/Form.cshtml script to populate dropdowns and auto-fill material group
        /// when a material is selected.
        /// </summary>
        [HttpGet]
        public JsonResult GetMaterialAndGroups()
        {
            try
            {
                var materials = db.MaterialMasters
                    .OrderBy(m => m.MTRLDESC)
                    .Select(m => new
                    {
                        id = m.MTRLID,
                        name = m.MTRLDESC,
                        groupId = m.MTRLGID,
                        rate = m.RATE,
                        profitPercent = m.MTRLPRFT
                    })
                    .ToList();

                var groups = db.MaterialGroupMasters
                    .OrderBy(g => g.MTRLGDESC)
                    .Select(g => new
                    {
                        id = g.MTRLGID,
                        name = g.MTRLGDESC
                    })
                    .ToList();

                return Json(new { success = true, materials, groups }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Returns active cost factors from COSTFACTORMASTER for use in the Sales Order TAX / Cost Factor popup.
        /// </summary>
        [HttpGet]
        public JsonResult GetCostFactorsForSales()
        {
            try
            {
                var items = db.CostFactorMasters
                    .Where(c => c.DISPSTATUS == 0)
                    .OrderBy(c => c.CFDESC)
                    .Select(c => new
                    {
                        id = c.CFID,
                        name = c.CFDESC
                    })
                    .ToList();

                return Json(new { success = true, items }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private void InsertDetails(TransactionMaster master, List<SalesOrderDetailRow> details)
        {
            if (details == null || !details.Any())
            {
                return;
            }

            var materialIds = details.Select(d => d.MaterialId).Distinct().ToList();
            var materials = db.MaterialMasters
                .Where(m => materialIds.Contains(m.MTRLID))
                .ToDictionary(m => m.MTRLID, m => m);
            var hsnIds = materials.Values
                .Where(m => m.HSNID > 0)
                .Select(m => m.HSNID)
                .Distinct()
                .ToList();

            var hsnMap = db.HSNCodeMasters
                .Where(h => hsnIds.Contains(h.HSNID))
                .ToDictionary(h => h.HSNID, h => h);

            decimal totalGross = 0m;
            decimal totalCgst = 0m;
            decimal totalSgst = 0m;
            decimal totalIgst = 0m;
            decimal totalNet = 0m;
            int lineIndex = 0;

            short tranStateType = master.TRANSTATETYPE;
            int tranMid = master.TRANMID;

            foreach (var d in details)
            {
                lineIndex++;

                materials.TryGetValue(d.MaterialId, out var material);

                int hsnId = material != null ? material.HSNID : 0;
                hsnMap.TryGetValue(hsnId, out var hsn);

                decimal qty = d.Qty;
                decimal rate = d.Rate;
                decimal gross = d.Amount > 0 ? d.Amount : qty * rate;

                decimal profitPercent = material != null ? material.MTRLPRFT : 0m;
                decimal actualRate = rate;
                if (rate > 0 && profitPercent != 0)
                {
                    actualRate = Math.Round(rate + ((rate * profitPercent) / 100m), 2);
                }

                decimal cgstAmt = 0m;
                decimal sgstAmt = 0m;
                decimal igstAmt = 0m;

                if (hsn != null)
                {
                    if (tranStateType == 0)
                    {
                        if (hsn.CGSTEXPRN > 0)
                        {
                            cgstAmt = Math.Round((gross * hsn.CGSTEXPRN) / 100m, 2);
                        }

                        if (hsn.SGSTEXPRN > 0)
                        {
                            sgstAmt = Math.Round((gross * hsn.SGSTEXPRN) / 100m, 2);
                        }
                    }
                    else
                    {
                        if (hsn.IGSTEXPRN > 0)
                        {
                            igstAmt = Math.Round((gross * hsn.IGSTEXPRN) / 100m, 2);
                        }
                    }
                }

                decimal net = gross + cgstAmt + sgstAmt + igstAmt;

                var detail = new TransactionDetail
                {
                    TRANMID = tranMid,
                    TRANDREFID = material != null ? material.MTRLID : d.MaterialId,
                    TRANDREFNO = material != null ? material.MTRLCODE : string.Empty,
                    TRANDREFNAME = material != null ? material.MTRLDESC : string.Empty,
                    TRANDMTRLPRFT = profitPercent,
                    HSNID = hsnId,
                    PACKMID = 0,
                    TRANDQTY = qty,
                    TRANDRATE = rate,
                    TRANDARATE = actualRate,
                    TRANDGAMT = gross,
                    TRANDCGSTAMT = cgstAmt,
                    TRANDSGSTAMT = sgstAmt,
                    TRANDIGSTAMT = igstAmt,
                    TRANDNAMT = net,
                    TRANDAID = lineIndex,
                    TRANDNARTN = null,
                    TRANDRMKS = null
                };

                db.TransactionDetails.Add(detail);

                totalGross += gross;
                totalCgst += cgstAmt;
                totalSgst += sgstAmt;
                totalIgst += igstAmt;
                totalNet += net;
            }

            master.TRANGAMT = totalGross;
            master.TRANCGSTAMT = totalCgst;
            master.TRANSGSTAMT = totalSgst;
            master.TRANIGSTAMT = totalIgst;
            master.TRANNAMT = totalNet;
            master.TRANPCOUNT = details.Count;
            master.TRANAMTWRDS = ConvertAmountToWords(totalNet);

            db.SaveChanges();
        }

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

        private class SalesOrderDetailRow
        {
            public int MaterialId { get; set; }
            public decimal Qty { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
            public decimal ProfitPercent { get; set; }
            public decimal ActualRate { get; set; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
