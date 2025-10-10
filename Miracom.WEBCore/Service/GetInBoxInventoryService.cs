using ASP_Entity_Freamwork_Study.Entity;
using System.Data;

namespace Miracom.WEBCore.Service
{
    public interface GetInBoxInventoryService
    {
        List<Dictionary<string, object>> GetInBoxInventory(string INNER_BOX_ID, string HPBS, string LOT_TYPE, string LOT_CMF_2, string DATA_2, string AUFNR, string LOT_CMF_5, string MAT_ID, string PROC_TYPE, string PO_CMF_14, string LOT_CMF_7, string WAFER, string WAFER_LOT_ID, string MARK, string CUST_PO_NO, string SALES_CODE, string ORDER_ID, string NHE, string QTY, string BIN, string UPDATE_USER, string UPDATE_TIME, string HH, string RECEIPT_ID, string LOT_ID);
    }
}
