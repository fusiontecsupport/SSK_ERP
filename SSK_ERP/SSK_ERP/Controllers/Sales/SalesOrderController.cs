using System;
using System.Linq;
using System.Web.Mvc;
using SSK_ERP.Filters;
using SSK_ERP.Models;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class SalesOrderController : Controller
    {
        private readonly ApplicationDbContext db = new ApplicationDbContext();

        [Authorize(Roles = "SalesOrderIndex")]
        public ActionResult Index()
        {
            return View();
        }

        [Authorize(Roles = "SalesOrderCreate,SalesOrderEdit")]
        public ActionResult Form()
        {
            return View();
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
