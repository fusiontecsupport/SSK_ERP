using System;
using System.Linq;
using System.Web.Mvc;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers.Purchase
{
    [SessionExpire]
    public class PurchaseOrderController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();

        [Authorize(Roles = "PurchaseOrderIndex")]
        public ActionResult Index()
        {
            return View();
        }

        [Authorize(Roles = "PurchaseOrderCreate,PurchaseOrderEdit")]
        public ActionResult Form()
        {
            return View();
        }

        /// <summary>
        /// Returns material and material group data for the Purchase Order detail grid.
        /// Used by the PurchaseOrder/Form.cshtml script to populate dropdowns and auto-fill
        /// material group when a material is selected.
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
                        groupId = m.MTRLGID
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
        /// Returns active cost factors from COSTFACTORMASTER for use in the Purchase Order TAX / Cost Factor popup.
        /// </summary>
        [HttpGet]
        public JsonResult GetCostFactorsForPurchase()
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

