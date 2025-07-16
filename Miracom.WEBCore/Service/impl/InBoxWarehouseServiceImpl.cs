using ASP_Entity_Freamwork_Study.Entity;
using ASP_Entity_Freamwork_Study.Utils;
using System.Data;

namespace Miracom.WEBCore.Service.impl
{
    public class InBoxWarehouseServiceImpl : InBoxWarehouseService
    {
        public List<Dictionary<string, object>> GetInBoxData(string boxId, string productModel, string pid, string poNo, string moNo, string lotId, string binGrade)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            try
            {
                string sql = $"SELECT T1.INNER_BOX_ID , T2.BOX_QTY, T1.SHELF_ID , T3.LOT_CMF_5 AS PROD_MODEL , T2.MAT_ID AS PID, T2.PO_NO , T2.MO_NO , T3.LOT_ID , T5.DATA_1 AS " +
                    $"BIN_GRADE ,T6.RECEIPT_ID, T6.CREATE_TIME FROM MESMGR.INNERBOXSHELFINFO T1 LEFT JOIN MESMGR.CTAPOBXSTS T2 ON T1.INNER_BOX_ID = T2.BOX_ID LEFT JOIN MESMGR.MWIPLOTSTS T3 ON T2.LOT_ID = T3.LOT_ID " +
                    $"LEFT JOIN MESMGR.MWIPBINSHS T4 ON T2.LOT_ID = T4.CHILD_LOT_ID AND T3.MAT_ID = T4.CHILD_MAT_ID " +
                    $"LEFT JOIN MESMGR.MGCMTBLDAT T5 ON T5.TABLE_NAME = 'BIN_LABEL_RELATION' AND T3.LOT_CMF_2 = T5.KEY_1 AND NVL(T4.BIN_PROMPT, 'HBIN1') = T5.KEY_2 " +
                    $"LEFT JOIN MESMGR.WAREHOUSERECEIPT T6 ON T1.INNER_BOX_ID = T6.INNER_BOX_ID " +
                    $"WHERE 1=1 AND LEAVE_WARE_HOUSE = '0' AND ('{boxId}' IS NULL OR T1.INNER_BOX_ID = '{boxId}') AND ('{productModel}' IS NULL OR T3.LOT_CMF_5 = '{productModel}') AND ('{pid}' IS NULL OR T2.MAT_ID = '{pid}') AND ('{poNo}' IS NULL OR T2.PO_NO = '{poNo}') AND ('{moNo}' IS NULL OR T2.MO_NO = '{moNo}') AND ('{lotId}' IS NULL OR T3.LOT_ID LIKE '%{lotId}%') AND ('{binGrade}' IS NULL OR T5.DATA_1 = '{binGrade}')";
                DataTable data = SqlUtils.SelectData(sql);
                foreach (DataRow row in data.Rows)
                {
                    Dictionary<string, object> dict = new Dictionary<string, object>();

                    foreach (DataColumn column in data.Columns)
                    {
                        if (column.ColumnName == "CREATE_TIME" && row[column] != DBNull.Value)
                        {
                            DateTime dt = Convert.ToDateTime(row[column]);
                            dict[column.ColumnName] = dt.ToString("yyyyMMddHHmmss");
                        }
                        else
                        {
                            dict[column.ColumnName] = row[column];
                        }
                    }

                    result.Add(dict);
                }


            }
            catch (Exception ex) { 
                Console.WriteLine(ex.ToString());
            }
            return result;
        }
    }
}
