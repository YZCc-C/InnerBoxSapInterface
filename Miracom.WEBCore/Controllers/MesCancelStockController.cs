using ASP_Entity_Freamwork_Study.Entity;
using Microsoft.AspNetCore.Mvc;
using Miracom.WEBCore.Service.impl;
using Miracom.WEBCore.Service;
using System.Data;

namespace Miracom.WEBCore.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MesCancelStockController
    {
        MesCancelStockService mesCancelStockService = new MesCancelStockServiceImpl();
        [HttpPost]
        public Result GetInBoxData(string? pickListNo, string? userId)
        {
            return mesCancelStockService.CancelStock(pickListNo, userId);
        }
    }
}
