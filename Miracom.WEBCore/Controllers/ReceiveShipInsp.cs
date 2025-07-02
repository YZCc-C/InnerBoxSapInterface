using ASP_Entity_Freamwork_Study.Entity;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Data;
using Oracle.ManagedDataAccess.Client;
using ASP_Entity_Freamwork_Study.Utils;
using Miracom.WEBCore.Service;
using Miracom.WEBCore.Service.impl;

namespace ASP_Entity_Freamwork_Study.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ReceiveShipInspController : ControllerBase
    {
        ReceiveShipInspService receiveShipInspService = new ReceiveShipInspServiceImpl();
        [HttpPost]
        public Result Post([FromBody] ReceiveInsp receiveInsp)

        {
            return receiveShipInspService.ShipInsp(receiveInsp);
        }
    }
}
