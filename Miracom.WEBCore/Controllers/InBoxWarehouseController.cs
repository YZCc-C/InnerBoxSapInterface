using ASP_Entity_Freamwork_Study.Entity;
using Microsoft.AspNetCore.Mvc;
using Miracom.WEBCore.Service.impl;
using Miracom.WEBCore.Service;
using System.Data;

namespace Miracom.WEBCore.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class InBoxWarehouseController
    {
        InBoxWarehouseService inBoxWarehouseService = new InBoxWarehouseServiceImpl();
        [HttpGet]
        public List<Dictionary<string,object>> GetInBoxData(string? boxId,string? productModel,string? pid,string? poNo,string? moNo,string? lotId,string? binGrade)

        {
            return inBoxWarehouseService.GetInBoxData(boxId,productModel,pid,poNo,moNo,lotId,binGrade);
        }
    }
}
