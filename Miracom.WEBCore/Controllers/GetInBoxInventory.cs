using Microsoft.AspNetCore.Mvc;
using Miracom.WEBCore.Service.impl;
using Miracom.WEBCore.Service;
using ASP_Entity_Freamwork_Study.Entity;
using System.Data;

namespace Miracom.WEBCore.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GetInBoxInventoryController
    {
        GetInBoxInventoryService getInBoxInventoryService = new GetInBoxInventoryServiceImpl();
        [HttpGet]
        public List<Dictionary<string, object>> GetInBoxInventory(string? INNER_BOX_ID, string? HPBS, string? LOT_TYPE, string? LOT_CMF_2, string? DATA_2, string? AUFNR, string? LOT_CMF_5, string? MAT_ID, string? PROC_TYPE, string? PO_CMF_14, string? LOT_CMF_7, string? WAFER, string? WAFER_LOT_ID, string? MARK, string? CUST_PO_NO, string? SALES_CODE, string? ORDER_ID, string? NHE, string? QTY, string? BIN, string? UPDATE_USER, string? UPDATE_TIME, string? HH, string? RECEIPT_ID, string? LOT_ID)

        {
            return getInBoxInventoryService.GetInBoxInventory(INNER_BOX_ID, HPBS, LOT_TYPE, LOT_CMF_2, DATA_2, AUFNR, LOT_CMF_5, MAT_ID, PROC_TYPE, PO_CMF_14, LOT_CMF_7, WAFER, WAFER_LOT_ID, MARK, CUST_PO_NO, SALES_CODE, ORDER_ID, NHE, QTY, BIN, UPDATE_USER, UPDATE_TIME, HH, RECEIPT_ID, LOT_ID);
        }
    }
}
