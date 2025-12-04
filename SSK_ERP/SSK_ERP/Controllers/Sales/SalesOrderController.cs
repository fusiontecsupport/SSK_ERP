using System.Web.Mvc;
using SSK_ERP.Filters;

namespace SSK_ERP.Controllers
{
    [SessionExpire]
    public class SalesOrderController : Controller
    {
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
    }
}

