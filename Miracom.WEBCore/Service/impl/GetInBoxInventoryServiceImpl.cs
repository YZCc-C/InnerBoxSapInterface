using ASP_Entity_Freamwork_Study.Entity;
using ASP_Entity_Freamwork_Study.Utils;
using System.Data;

namespace Miracom.WEBCore.Service.impl
{
    public class GetInBoxInventoryServiceImpl : GetInBoxInventoryService
    {
        public List<Dictionary<string, object>> GetInBoxInventory(string INNER_BOX_ID, string HPBS, string LOT_TYPE, string LOT_CMF_2, string DATA_2, string AUFNR, string LOT_CMF_5, string MAT_ID, string PROC_TYPE, string PO_CMF_14, string LOT_CMF_7, string WAFER, string WAFER_LOT_ID, string MARK, string CUST_PO_NO, string SALES_CODE, string ORDER_ID, string NHE, string QTY, string BIN, string UPDATE_USER, string UPDATE_TIME, string HH, string RECEIPT_ID, string LOT_ID)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            try
            {
                string sql = $"SELECT UA.INNER_BOX_ID,\r\nUA.HPBS,\r\n " +
                    $"      UC.LOT_TYPE,\r\n       UC.LOT_CMF_2,\r\n      " +
                    $" UE.DATA_2,\r\n       UE.AUFNR,\r\n       UC.LOT_CMF_5,\r\n " +
                    $"      UC.MAT_ID,\r\n       UD.PROC_TYPE,\r\n       UD.PO_CMF_14,\r\n" +
                    $"       UC.LOT_CMF_7,\r\n       UG.WAFER,\r\n       UG.WAFER_LOT_ID,\r\n   " +
                    $"    UG.mark,\r\n       UD.CUST_PO_NO,\r\n       UD.SALES_CODE,\r\n       UC.ORDER_ID,\r\n " +
                    $"      UA.INNER_BOX_ID NHE,\r\n       UA.QTY,\r\n      " +
                    $" CASE\r\n         WHEN SUBSTR(UA.LOT_ID,-1, 3) = '.B2' THEN\r\n          'BIN2'\r\n       " +
                    $"  WHEN SUBSTR(UA.LOT_ID,-1, 3) = '.B3' THEN\r\n          'BIN3'\r\n         ELSE\r\n          'BIN1'\r\n      " +
                    $" END BIN,\r\n       UA.UPDATE_USER,\r\n       UA.UPDATE_TIME,\r\n       UA.HH,\r\n       UA.RECEIPT_ID,\r\n       UA.LOT_ID\r\n\r\n  " +
                    $"FROM (\r\nSELECT\r\nUA.INNER_BOX_ID,\r\nUB.FLOT LOT_ID,\r\n         CASE\r\n           " +
                    $"WHEN UC.LOT_DEL_FLAG = 'Y' THEN\r\n            UA.QTY - UD.OLD_QTY_1\r\n           " +
                    $"WHEN UD.LOT_ID IS NOT NULL THEN\r\n            UD.OLD_QTY_1\r\n           ELSE\r\n            QTY\r\n        " +
                    $" END QTY,\r\nUA.UPDATE_USER,\r\nUA.UPDATE_TIME,\r\nUA.HH,\r\nUA.RECEIPT_ID,\r\nCASE WHEN OLD_QTY_1 >0 \r\n" +
                    $"THEN  'Y'\r\nELSE UC.LOT_DEL_FLAG\r\nEND HPBS\r\nFROM\r\n(SELECT A.LOT_ID,\r\nC.ORDER_ID,\r\n              " +
                    $" A.INNER_BOX_ID,\r\n               A.QTY,\r\n               A.UPDATE_USER,\r\n               A.UPDATE_TIME,\r\n              " +
                    $" ROUND(TO_CHAR(SYSDATE - TO_DATE(A.UPDATE_TIME, 'YYYY/MM/DD HH24:MI:SS')) * 24,2) HH,\r\n               A.RECEIPT_ID\r\n         " +
                    $" FROM MESMGR.WAREHOUSERECEIPT A,MESMGR.INNERBOXSHELFINFO B, MESMGR.MWIPLOTSTS C\r\n        " +
                    $" WHERE A.INNER_BOX_ID = B.INNER_BOX_ID\r\n\t\t\t\t AND A.LOT_ID = C.LOT_ID\r\n          " +
                    $" AND A.FAC_ID = 'TEST_NB'\r\n           AND STATE = '4'\r\n           AND C.LOT_CMF_2 IN ({LOT_CMF_2})\r\n           " +
                    $"AND B.LEAVE_WARE_HOUSE = '0'\r\n\t\t\t\t\t ) UA\r\n         " +
                    $" LEFT JOIN (SELECT LOT_ID LOT, FROM_TO_LOT_ID FLOT, OPER OP\r\n                     " +
                    $" FROM MESMGR.MWIPLOTMRG\r\n                     " +
                    $"WHERE FACTORY IN ('TEST_NB', 'ASSY_NB')\r\n                      " +
                    $" AND FROM_TO_FLAG = 'T'\r\n                      " +
                    $" AND HIST_DEL_FLAG = ' '\r\n                     " +
                    $"  AND FROM_TO_LOT_ID NOT LIKE '%.DB%'\r\n            " +
                    $"           AND SUBSTR(LOT_ID, 1, 4) IN ({LOT_CMF_2})\r\n       " +
                    $"                AND SUBSTR(LOT_ID, 1, 14) <>\r\n                       " +
                    $"    SUBSTR(FROM_TO_LOT_ID, 1, 14)\r\n                   " +
                    $" UNION ALL\r\n                    SELECT LOT_ID LOT, LOT_ID FLOT, OPER\r\n     " +
                    $"                 FROM MESMGR.MWIPLOTSTS\r\n                    " +
                    $" WHERE FACTORY IN ('TEST_NB', 'ASSY_NB')\r\n                   " +
                    $"    AND FROM_TO_LOT_ID NOT LIKE '%.DB%'\r\n                     " +
                    $"  AND LOT_CMF_2 IN ({LOT_CMF_2})) UB\r\n         " +
                    $"   ON UA.LOT_ID = UB.LOT\r\n          " +
                    $"LEFT JOIN (SELECT LOT_ID, ORDER_ID MO, OPER_IN_QTY_1, LOT_DEL_FLAG\r\n                    " +
                    $"  FROM MESMGR.MWIPLOTSTS\r\n                   " +
                    $"  WHERE LOT_CMF_2 IN ({LOT_CMF_2})\r\n                    ) UC\r\n        " +
                    $"    ON UB.FLOT = UC.LOT_ID\r\n           AND UA.ORDER_ID <> UC.MO\r\n        " +
                    $"  LEFT JOIN (SELECT MA.LOT_ID, OLD_QTY_1\r\n                   " +
                    $"   FROM (SELECT LOT_ID, MIN(HIST_SEQ) HIST\r\n                           " +
                    $"   FROM MESMGR.MWIPLOTMRG\r\n                             " +
                    $"WHERE FACTORY IN ('ASSY_NB', 'TEST_NB')\r\n                       " +
                    $"        AND HIST_DEL_FLAG = ' '\r\n                            " +
                    $"   AND FROM_TO_FLAG = 'T'\r\n                               " +
                    $"AND FROM_TO_LOT_ID NOT LIKE '%.DB%'\r\n                         " +
                    $"      AND SUBSTR(LOT_ID, 1, 4) IN ({LOT_CMF_2})\r\n      " +
                    $"                         AND SUBSTR(LOT_ID, 1, 14) <>\r\n                    " +
                    $"               SUBSTR(FROM_TO_LOT_ID, 1, 14)\r\n                           " +
                    $"  GROUP BY LOT_ID) MA\r\n                      " +
                    $"LEFT JOIN (SELECT LOT_ID, OLD_QTY_1, HIST_SEQ\r\n          " +
                    $"                        FROM MESMGR.MWIPLOTHIS\r\n                        " +
                    $"         WHERE LOT_CMF_2 IN ({LOT_CMF_2})\r\n            " +
                    $"                       AND TRAN_CODE = 'MERGE'\r\n                         " +
                    $"          AND HIST_DEL_FLAG = ' '\r\n                                " +
                    $"   AND FACTORY IN ('ASSY_NB', 'TEST_NB')\r\n                                ) MB\r\n    " +
                    $"                    ON MA.LOT_ID = MB.LOT_ID\r\n                     " +
                    $"  AND MA.HIST = MB.HIST_SEQ) UD\r\n            ON UA.LOT_ID = UD.LOT_ID\r\n        ) UA\r\n  " +
                    $"LEFT JOIN (SELECT LOT_ID LOT,\r\n                    " +
                    $"LOT_TYPE,\r\n                   " +
                    $" LOT_CMF_2,\r\n                   " +
                    $" LOT_CMF_5,\r\n                   " +
                    $" MAT_ID,\r\n                    " +
                    $"LOT_CMF_7,\r\n                    " +
                    $"ORDER_ID\r\n               " +
                    $"FROM MESMGR.MWIPLOTSTS\r\n              " +
                    $"WHERE FACTORY IN ('TEST_NB', 'FGS_NB')\r\n                " +
                    $"and LOT_CMF_2 IN ({LOT_CMF_2}) \r\n             ) UC\r\n   " +
                    $" ON UA.LOT_ID = UC.LOT\r\n  LEFT JOIN (SELECT PO_NO, PO_CMF_12, CUST_PO_NO, PROC_TYPE, SALES_CODE, PO_CMF_14\r\n              " +
                    $" FROM MESMGR.MTAPCPOSTS\r\n             " +
                    $" WHERE PO_CMF_19 = '1110'\r\n                and customer_id IN ({LOT_CMF_2})\r\n\r\n                         ) UD\r\n   " +
                    $" ON UC.ORDER_ID = UD.PO_NO\r\n  LEFT JOIN (SELECT KEY_1, DATA_2, substr(DATA_1,5) AUFNR\r\n           " +
                    $"    FROM MGCMTBLDAT\r\n              WHERE TABLE_NAME = 'B@CUSTOMER'\r\n              " +
                    $"  AND FACTORY = 'SYSTEM'\r\n                and KEY_1 IN ({LOT_CMF_2})) UE\r\n   " +
                    $" ON UC.LOT_CMF_2 = UE.KEY_1\r\n  LEFT JOIN (SELECT DISTINCT T.PO_NO,\r\n                  " +
                    $"           replace(MK.data_1,'@',chr(10)) mark,\r\n                          " +
                    $"    CASE\r\n                               " +
                    $" WHEN T2.DATA_3 <> ' ' THEN\r\n                                 " +
                    $"T2.DATA_3\r\n                               " +
                    $" ELSE\r\n                                 " +
                    $"T.WAFER_LOT_ID\r\n                              " +
                    $"END WAFER_LOT_ID,\r\n                            " +
                    $" CASE\r\n                              " +
                    $" WHEN SUBSTR(T.PO_CMF_1, 0, 3) = 'C01' THEN\r\n                                " +
                    $"NVL(WAFER.MAT_SHORT_DESC, T3.WAFERTYPE)\r\n                               " +
                    $"ELSE\r\n                                T.PO_CMF_1\r\n                             " +
                    $"END WAFER\r\n             \r\n               " +
                    $"FROM MESMGR.MTAPCPOSTS T,\r\n                    " +
                    $"(SELECT DISTINCT T.MAT_ID, T.MAT_DESC, T.MAT_SHORT_DESC\r\n                       " +
                    $"FROM MESMGR.MTIVMATDEF T\r\n                     " +
                    $" WHERE MAT_TYPE = '2001'\r\n                       " +
                    $" AND FACTORY = 'ASSY_NB') WAFER,\r\n                    " +
                    $"(SELECT *\r\n                       " +
                    $"FROM MESMGR.MGCMLAGDAT\r\n                      " +
                    $"WHERE TABLE_NAME = 'MTAPCPOLGD'\r\n                       " +
                    $" AND FACTORY = 'DIEBANK_NB') T2,\r\n                   " +
                    $" (SELECT WAFERTYPE, WAFERID, CUSTOMER, WAFERPIECE\r\n                      " +
                    $" FROM MESMGR.TTRSWAFSTK) T3,\r\n                    " +
                    $"(SELECT DISTINCT ORDER_ID,\r\n                       " +
                    $" DATA_1\r\n          FROM (SELECT FACTORY,\r\n             " +
                    $"          KEY_1,\r\n                      " +
                    $" CASE\r\n                         " +
                    $"WHEN FACTORY = 'TEST_NB'\r\n                              " +
                    $"AND DATA_1 = 'X' THEN\r\n                          ' '\r\n                         " +
                    $"WHEN DATA_1 IN ('只打pin1点', '只打印圆点') THEN\r\n                          '●'\r\n                         ELSE\r\n                          " +
                    $"DATA_1\r\n                       END DATA_1,\r\n                       " +
                    $"MAX(CREATE_TIME) CREATE_TIME\r\n                  FROM MESMGR.MGCMLAGDAT\r\n                " +
                    $" WHERE \r\n                 TABLE_NAME = 'MK_CONTENT_WEBAPI'\r\n                   " +
                    $"AND FACTORY IN ('ASSY_NB', 'TEST_NB')\r\n                   AND DATA_1 <> ' '\r\n                " +
                    $" GROUP BY FACTORY,\r\n                          KEY_1,\r\n                          DATA_1) S,\r\n               " +
                    $"(SELECT DISTINCT LOT_ID,\r\n                               " +
                    $" ORDER_ID\r\n                  " +
                    $"FROM MESMGR.MWIPLOTSTS\r\n                 " +
                    $"WHERE FACTORY IN\r\n                       " +
                    $"('DIEBANK_NB', 'ASSY_NB', 'TEST_NB', 'FGS_NB')) T\r\n         " +
                    $"WHERE S.KEY_1 = T.LOT_ID(+)\r\n           " +
                    $"AND DATA_1 <> ' '\r\n           " +
                    $"AND ORDER_ID IS NOT NULL) MK\r\n             " +
                    $" WHERE T.PO_NO = T2.KEY_1\r\n                " +
                    $"AND T.PO_CMF_1 = WAFER.MAT_ID(+)\r\n                " +
                    $"AND T2.DATA_3 = T3.WAFERID(+)\r\n                " +
                    $"AND T2.DATA_2 = T3.WAFERPIECE(+)\r\n                " +
                    $"AND T.CUSTOMER_ID = T3.CUSTOMER(+)\r\n                " +
                    $"and T.PO_NO = MK.ORDER_ID\r\n                " +
                    $"and T.customer_id IN ({LOT_CMF_2})) UG\r\n    " +
                    $"ON UC.ORDER_ID = UG.PO_NO\r\n    \r\n ORDER BY UC.LOT_CMF_2, UC.ORDER_ID,UA.INNER_BOX_ID";
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
