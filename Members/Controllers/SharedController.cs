using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Members.Models;
using System.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Members.Controllers
{
    public class CombosController : Controller // Combos Controller
    {
        protected IActionResult CustomView(string viewName)
        {
            ViewBag.Message = "Combos";
            ViewData["ViewName"] = viewName;
            return View("Slideshows");
        }
        public IActionResult Index() => CustomView("Combos");
    }

    public class SlideshowsController : Controller  //Slideshows Controller
    {
        protected IActionResult CustomView(string viewName)
        {
            ViewBag.Message = "Slideshows";
            ViewData["ViewName"] = viewName;
            return View("Slideshows");
        }
        public IActionResult Index() => CustomView("Slideshows");
    }
    public class GalleriesController : Controller  // Galleries Controller
    {
        protected IActionResult CustomView(string viewName)
        {
            ViewBag.Message = "Galleries";
            ViewData["ViewName"] = viewName;
            return View("Galleries");
        }
        public IActionResult Index() => CustomView("Galleries");
    }
    public class GalleryBaseController : Controller
    {
        public IActionResult Index() => GalleryView();
        public IActionResult Gallery(string viewName) => GalleryView(viewName);
        protected IActionResult GalleryView(string imagefolder = "Gallery")
        {
            ViewBag.Message = "Gallery";
            ViewData["ViewName"] = imagefolder;
            return View();
        }
    }

    public class OaksController : GalleryBaseController { }    
    public class OaksSlideController : GalleryBaseController { }
    public class OaksComboController : GalleryBaseController { }

    //public class OfficeViewController : GalleryBaseController { }
    //public class OfficeViewSlideController : GalleryBaseController { }
    //public class OfficeViewComboController : GalleryBaseController { }

}
