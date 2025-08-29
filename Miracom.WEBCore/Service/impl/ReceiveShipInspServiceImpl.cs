using ASP_Entity_Freamwork_Study.Entity;
using ASP_Entity_Freamwork_Study.Utils;
using Miracom.WEBCore.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Data.SqlClient;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Text.RegularExpressions;
using static System.Runtime.CompilerServices.RuntimeHelpers;

namespace Miracom.WEBCore.Service.impl
{
    public class ReceiveShipInspServiceImpl : ReceiveShipInspService
    {
        #region  全局变量
        private DataTable mergeBoxRules = new DataTable(); // 并箱规则集合
        private DataRow firstBox; // 首箱
        private HashSet<string> dateCodes = new HashSet<string>(); // DateCode数量
        private HashSet<string> waferLots = new HashSet<string>();// 晶圆批次数量
        private HashSet<string> lblLots = new HashSet<string>();// 标签批次数量
        private HashSet<string> cgwDateCodes = new HashSet<string>();// 标签批次数量
        private HashSet<string> marks = new HashSet<string>();// Mark栏位数量

        private Dictionary<string,DataTable> lotPoInfo = new Dictionary<string,DataTable>(); // 获取多批次，和对应的Po
        private Dictionary<string, bool> isFang = new Dictionary<string, bool>(); // 判断数量类型的规则时，通过批次看放不放的进去
        int currentQty = 0; // 出的数量
        int boxQty = 0; // // 
        private Dictionary<string, DataTable> deptBoxs = new Dictionary<string, DataTable>();  // 尾箱集合
        private string inboxSql = $"SELECT T2.LOT_ID, T1.SHELF_ID , T2.BOX_ID , T2.PO_NO, T2.MO_NO, T2.BOX_QTY , CASE WHEN T3.STANDARD_QTY * T7.STANDARD_QTY - T2.BOX_QTY > 0 THEN 'N' ELSE 'Y' END AS IS_FULL, T5.LOT_CMF_5 , T4.PO_TIME , T4.CUST_MAT_ID , T5.MAT_ID AS PID , T6.LOT_ID AS LOT_ID2, CASE WHEN T3.STANDARD_QTY  - T8.IN_BOX_QTY > 0 THEN 'N' ELSE 'Y' END AS IS_REEL_FULL FROM MESMGR.INNERBOXSHELFINFO T1 LEFT JOIN MESMGR.CTAPOBXSTS T2 ON T1.INNER_BOX_ID = T2.BOX_ID LEFT JOIN MESMGR.MTAPCPOSTS T4 ON T2.PO_NO = T4.PO_NO LEFT JOIN MESMGR.MWIPLOTSTS T5 ON T2.LOT_ID = T5.LOT_ID LEFT JOIN MESMGR.WAREHOUSERECEIPT T6 ON T1.INNER_BOX_ID = T6.INNER_BOX_ID LEFT JOIN MESMGR.MTAPMATLBL T3 ON T5.MAT_ID = T3.MAT_ID AND T3.HALFBOX_FLAG = 'Y' LEFT JOIN MESMGR.MTAPMATLBL T7 ON T5.MAT_ID = T7.MAT_ID AND T7.INBOX_FLAG = 'Y' LEFT JOIN (SELECT OUTBOX_ID , COUNT(*) AS IN_BOX_QTY  FROM MESMGR.COBXIBXSTS WHERE OUT_BOX_TYPE = 'INNER' GROUP BY OUTBOX_ID) T8 ON T1.INNER_BOX_ID = T8.OUTBOX_ID WHERE T1.LEAVE_WARE_HOUSE = 0 AND ? ORDER BY T2.BOX_QTY DESC , T4.PO_NO, T5.LOT_ID, T1.WARE_HOUSE_ENTER_TIME , SHELF_ID ASC";  // 查内盒库对应内盒以及它属性的SQL
        private bool flagRuleType = false;   //是否有最大不超过类型的
        private int maxQty = 0; // 数量
        private bool shipMethod = true; //  true  内盒必须满数  false 内盒必须满卷
        public class DataModel
        {
            public string pid { get; set; }
            public string eqp_ID { get; set; }
        }
        #endregion

        #region 接收指令 实现
        public Result ShipInsp(ReceiveInsp receiveInsp)
        {
            Result result = new Result();
            try
            {

                AllClear();
                // 出货方式：是满卷还是满箱

                //接收先判定
                switch (receiveInsp.type)
                {
                    case 1: //指定型号，并指定数量，考虑到还要通过数量过滤，并箱规则获取
                        shipMethod = SqlUtils.SelectData($"SELECT CUSTOMER_ID, EVENT, EVENT_KEY, CREATE_TIME, CREATE_USER, UPDATE_TIME, UPDATE_USER, FAC_ID FROM MESMGR.OUTBOXPRINTIDYN WHERE CUSTOMER_ID = '{receiveInsp.consumerId}' AND TYPE = 'FULL'").Rows.Count > 0;
                        result = GenerateByProductType(receiveInsp);
                        break;
                    case 2: // 指定 PO号清线
                        result = GenerateByPoNo(receiveInsp);
                        break;
                    case 3: // 指定 MO号清线
                        result = GenerateByMoNo(receiveInsp);
                        break;
                    case 4: // 指定 sub_lot 或内盒号
                        result = GenerateBySubLot(receiveInsp);
                        break;
                    case 5:
                        result = GenerateByInBoxId(receiveInsp);
                        break;
                    default: // 不存在该种指令，报凑
                        result.code = 500;
                        result.message = $"不存在类型为{receiveInsp.type}的指令";
                        break;
                }

                DataTable dt = (DataTable)result.data;
                OperationDb(result,receiveInsp,dt);
                result.data = null;
            }
            catch (Exception ex) {
                Console.WriteLine(ex.ToString());
                result.code = 500;
                result.message = "失败";
            }
            return result;
        }

        #endregion

        #region 做数据库操作
        private async void OperationDb(Result result, ReceiveInsp receiveInsp, DataTable dt)
        {
            await Task.Run(() => {

                if (result.code == 200)
                {
                    int outBoxQty = dt.Rows.Count / boxQty + (dt.Rows.Count % boxQty > 0 ? 1 : 0);
                    // 插入出货指令
                    string sql = $"INSERT INTO MESMGR.SHIPINSP ( SHIP_NO, INSP_TYPE, QTY, CONSUMER_ID, MO_NO, PO_NO, SUB_LOT_ID, SHIP_DATE, PROD_MODEL, BOX_ID,EXPECT_SHIP_TIME,SHIP_BOX_QTY) VALUES( '{receiveInsp.shipNo}' ,{receiveInsp.type}, {receiveInsp.qty}, '{receiveInsp.consumerId}', '{receiveInsp.moNo}', '{receiveInsp.poNo}', '{receiveInsp.subLotId}', '{DateTime.Now}', '{receiveInsp.prodModel}','{receiveInsp.boxId}','{receiveInsp.expectShipTime}', {outBoxQty})";
                    SqlUtils.ExcuteSingleCRD(sql);
                    // 计算有几个外箱

                    int count = 0; // 计数器，放到第几个内盒了
                    foreach (DataRow dr in dt.Rows)
                    {
                        count++;
                        string boxOfQty = (count / boxQty + (count % boxQty > 0 ? 1 : 0)) + " of " + outBoxQty;
                        if (!dr["BOX_ID"].ToString().Equals("空箱"))
                        {
                            string insertSql = $"INSERT INTO MESMGR.PICKLIST (PICK_LIST_NO, OUT_BOX_ID, BOX_OF_QTY, IN_BOX_ID, SHELF_ID, QTY, PO_NO, TRS_LOT_ID, CUST_LOT_ID, DATE_CODE, DEVICE, CUSTOM_ID, EVENT, EVENT_KEY, CREATE_TIME, CREATE_USER, UPDATE_TIME, UPDATE_USER, FAC_ID, IS_PRINT,PID) VALUES('{receiveInsp.shipNo}', ' ', '{boxOfQty}', '{dr["BOX_ID"]}', '{dr["SHELF_ID"]}', {dr["BOX_QTY"]}, '{dr["PO_NO"]}', '{dr["LOT_ID2"]}', '{dr["CUST_LOT_ID"]}', '{dr["LOT_ID"].ToString().Substring(4, 4)}', '{dr["DEVICE"]}', '{receiveInsp.consumerId}', 'Created', '{DateTime.Now.ToString("yyyyMMddHHmmssfff")}', '{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}', 'SAP_USER', '{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}', 'SAP_USER', 'TEST_NB', '','{dr["PID"]}')";
                            SqlUtils.ExcuteSingleCRD(insertSql);
                            // 更新内赫库中盒子状态
                            SqlUtils.ExcuteSingleCRD($"UPDATE MESMGR.INNERBOXSHELFINFO SET LEAVE_WARE_HOUSE = '1' WHERE INNER_BOX_ID = '{dr["BOX_ID"]}'");
                        }

                    }
                }
            });
        }

        #endregion

        #region 指令类型 1  指定型号与数量
        public Result GenerateByProductType(ReceiveInsp shipInsp)
        {
            try
            {
                Result result = new Result();
                string prodModel = shipInsp.prodModel;
                int shipQty = shipInsp.qty;
                //1.获取该 产品型号 外箱对应的几个内箱
                string getQtySql = $"SELECT DISTINCT T4.STANDARD_QTY FROM MESMGR.INNERBOXSHELFINFO T1 LEFT JOIN ( SELECT OUTBOX_ID , LOT_ID , PO_NO , MO_NO , SUM(INBOX_QTY) AS BOX_QTY FROM MESMGR.COBXIBXSTS c WHERE OUT_BOX_TYPE = 'INNER' GROUP BY OUTBOX_ID , LOT_ID , PO_NO , MO_NO , OUT_BOX_QTY) T2 ON T1.INNER_BOX_ID = T2.OUTBOX_ID LEFT JOIN MESMGR.MWIPLOTSTS T3 ON T2.LOT_ID = T3.LOT_ID LEFT JOIN ( SELECT MAT_ID , STANDARD_QTY FROM MESMGR.MTAPMATLBL WHERE OUTBOX_FLAG = 'Y') T4 ON T3.MAT_ID = T4.MAT_ID WHERE T3.LOT_CMF_5 = '{prodModel}'";
                System.Data.DataTable dtQty = SqlUtils.SelectData(getQtySql);
                if (dtQty.Rows.Count == 0)
                {
                    result.code = 500;
                    result.message = "错误，当前产品未维护装箱数量";
                    return result;
                }
                // 一个外箱最多装几个内盒
                boxQty = int.Parse(dtQty.Rows[0][0].ToString());
                // 查出内盒库中所有符合条件的内盒
                //   string inboxSql = $"SELECT T2.LOT_ID, T1.SHELF_ID , T2.OUTBOX_ID, T2.PO_NO, T2.MO_NO, T2.LOT_QTY, T3.IS_FULL , T4.CUST_LOT_ID , T4.PO_TIME ,T4.CUST_MAT_ID , T6.LOT_ID AS LOT_ID2 , T2.MO_NO, T5.BOX_QTY  FROM MESMGR.INNERBOXSHELFINFO T1 LEFT JOIN ( SELECT OUTBOX_ID , LOT_ID , PO_NO , MO_NO , SUM(INBOX_QTY) AS LOT_QTY FROM MESMGR.COBXIBXSTS c WHERE OUT_BOX_TYPE = 'INNER' GROUP BY OUTBOX_ID , LOT_ID , PO_NO , MO_NO , OUT_BOX_QTY) T2 ON T1.INNER_BOX_ID = T2.OUTBOX_ID LEFT JOIN ( SELECT CASE WHEN STANDARD_QTY - BB.BOX_QTY > 0 THEN 'N' ELSE 'Y' END AS IS_FULL, BB.OUTBOX_ID FROM MESMGR.MTAPMATLBL AA LEFT JOIN ( SELECT COUNT(*) AS BOX_QTY , OUTBOX_ID , MAT_ID FROM MESMGR.COBXIBXSTS c WHERE OUT_BOX_TYPE = 'INNER' GROUP BY OUTBOX_ID, MAT_ID ) BB ON AA.MAT_ID = BB.MAT_ID WHERE AA.HALFBOX_FLAG = 'Y' ) T3 ON T1.INNER_BOX_ID = T3.OUTBOX_ID LEFT JOIN ( SELECT PO_NO , CUST_LOT_ID , PO_TIME, CUST_MAT_ID FROM MESMGR.MTAPCPOSTS GROUP BY PO_NO , CUST_LOT_ID , PO_TIME, CUST_MAT_ID) T4 ON T2.PO_NO = T4.PO_NO LEFT JOIN MWIPLOTSTS T5 ON T2.LOT_ID  = T5.LOT_ID  LEFT JOIN MESMGR.CTAPOBXSTS T5 ON T1.INNER_BOX_ID = T5.BOX_ID  LEFT JOIN MESMGR.WAREHOUSERECEIPT T6 ON T1.INNER_BOX_ID  = T6.INNER_BOX_ID  WHERE T1.LEAVE_WARE_HOUSE = '0' AND T4.CUST_MAT_ID = '{prodModel}' ORDER BY T3.IS_FULL DESC, T2.LOT_QTY DESC , SHELF_ID ASC"
                if (shipInsp.consumerId.Equals(""))
                {
                    result.code = 500;
                    result.message = "错误，型号需要传客户号";
                    return result;
                }
                string param = $"T4.CUST_MAT_ID = '{prodModel}'AND LOT_CMF_2 = '{shipInsp.consumerId}' ";
                inboxSql = inboxSql.Replace("?", param);
                System.Data.DataTable dtBox = SqlUtils.SelectData(inboxSql);

                if (dtBox.Rows.Count == 0)
                {
                    result.code = 500;
                    result.message = "当前内盒库中没有符合条件的内盒";
                    return result;
                }

                // 通过并向规则过滤出所有能用的内盒
                DataTable dtBoxFifer = FifterByRule(dtBox, boxQty, shipInsp);
                if (dtBoxFifer == null)
                {
                    if (maxQty == 0)
                    {
                        // 客户没有维护并箱规则
                        result.code = 500;
                        result.message = "该客户没有维护并箱规则";
                        return result;

                    }
                    if (currentQty != shipInsp.qty)
                    {
                        result.code = 500;
                        result.message = $"当前内盒库中该型号无法出{shipInsp.qty},建议出{maxQty}";
                        return result;
                    }

                }
                dtBox = dtBoxFifer;
                result.code = 200;
                result.data = dtBox;
                result.message = "发送成功";
                return result;
            }
            catch (Exception ex)
            {
                Result result = new Result();
                result.code = 500;
                result.message = "系统错误";
                return result;
            }
        }
        #endregion

        #region 指令类型 2  指定PO号清线
        private Result GenerateByPoNo(ReceiveInsp shipInsp)
        {
            try
            {
                Result result = new Result();
                string poNo = shipInsp.poNo;
                //1.获取该 Po号 外箱对应的几个内箱
                string getQtySql = $"SELECT T2.STANDARD_QTY FROM ( SELECT  PO_NO ,  MO_NO ,  MAT_ID FROM  MESMGR.CTAPOBXSTS GROUP BY  PO_NO ,  MO_NO ,  MAT_ID) T1  LEFT JOIN ( SELECT  MAT_ID ,  STANDARD_QTY FROM  MESMGR.MTAPMATLBL WHERE  OUTBOX_FLAG = 'Y') T2  ON T1.MAT_ID = T2.MAT_ID WHERE T1.MO_NO = '{poNo}'";
                System.Data.DataTable dtQty = SqlUtils.SelectData(getQtySql);
                if (dtQty.Rows.Count == 0)
                {
                    result.code = 500;
                    result.message = "错误，当前产品未维护装箱数量";
                    return result;
                }
                // 一个外箱最多装几个内盒
                boxQty = int.Parse(dtQty.Rows[0][0].ToString());
                // 查出内盒库中所有符合条件的内盒
                string param = $"T2.MO_NO = '{poNo}'";
                inboxSql = inboxSql.Replace("?", param);
                System.Data.DataTable dtBox = SqlUtils.SelectData(inboxSql);

                if (dtBox.Rows.Count == 0)
                {
                    result.code = 500;
                    result.message = "当前内盒库中没有符合条件的内盒";
                    return result;
                }

                // 通过并向规则过滤出所有能用的内盒
                DataTable dtBoxFifer = FifterByRuleToOther(dtBox, boxQty, shipInsp);
                if (dtBoxFifer == null)
                {
                    result.code = 500;
                    result.message = "当前内盒库中没有符合条件的内盒";
                    return result;

                }
                dtBox = dtBoxFifer;
                result.code = 200;
                result.data = dtBox;
                result.message = "发送成功";
                return result;
            }
            catch (Exception ex)
            {
                Result result = new Result();
                result.code = 500;
                result.message = "系统错误";
                return result;
            }

        }
        #endregion

        #region 指令类型 3  指定MO号清线
        private Result GenerateByMoNo(ReceiveInsp shipInsp)
        {
            try
            {
                Result result = new Result();
                string moNo = shipInsp.moNo;
                //1.获取该 产品型号 外箱对应的几个内箱
                string getQtySql = $"SELECT T2.STANDARD_QTY FROM ( SELECT PO_NO ,MO_NO ,MAT_ID FROM MESMGR.CTAPOBXSTS GROUP BY PO_NO ,MO_NO ,MAT_ID  ) T1 LEFT JOIN MESMGR.MTAPMATLBL T2 ON T1.MAT_ID = T2.MAT_ID WHERE T1.PO_NO = '{moNo}' AND T2.OUTBOX_FLAG = 'Y' GROUP BY T2.STANDARD_QTY";
                System.Data.DataTable dtQty = SqlUtils.SelectData(getQtySql);
                if (dtQty.Rows.Count == 0)
                {
                    result.code = 500;
                    result.message = "错误，当前产品未维护装箱数量";
                    return result;
                }
                // 一个外箱最多装几个内盒
                boxQty = int.Parse(dtQty.Rows[0][0].ToString());
                string param = $"T2.PO_NO = '{moNo}'";
                inboxSql = inboxSql.Replace("?", param);
                // 查出内盒库中所有符合条件的内
                System.Data.DataTable dtBox = SqlUtils.SelectData(inboxSql);

                if (dtBox.Rows.Count == 0)
                {
                    result.code = 500;
                    result.message = "当前内盒库中没有符合条件的内盒";
                    return result;
                }

                // 通过并向规则过滤出所有能用的内盒
                DataTable dtBoxFifer = FifterByRuleToOther(dtBox, boxQty, shipInsp);
                if (dtBoxFifer == null)
                {
                    result.code = 500;
                    result.message = "当前内盒库中没有符合条件的内盒，或没有绑定至少一条并箱规则";
                    return result;

                }
                dtBox = dtBoxFifer;
                result.code = 200;
                result.data = dtBox;
                result.message = "发送成功";
                return result;
            }
            catch (Exception ex)
            {
                Result result = new Result();
                result.code = 500;
                result.message = "系统错误";
                return result;
            }
        }
        #endregion

        #region 指令类型 4  指定批次号
        private Result GenerateBySubLot(ReceiveInsp shipInsp)
        {
            try
            {
                Result result = new Result();
                string subLotId = shipInsp.subLotId;
                //1.获取该 产品型号 外箱对应的几个内箱
                string getQtySql = $"SELECT T2.STANDARD_QTY FROM ( SELECT PO_NO ,MO_NO ,MAT_ID ,LOT_ID  FROM MESMGR.CTAPOBXSTS GROUP BY PO_NO ,MO_NO ,MAT_ID ,LOT_ID ) T1 LEFT JOIN MESMGR.MTAPMATLBL T2 ON T1.MAT_ID = T2.MAT_ID WHERE T1.LOT_ID = '{subLotId}' AND T2.OUTBOX_FLAG = 'Y' GROUP BY T2.STANDARD_QTY";
                System.Data.DataTable dtQty = SqlUtils.SelectData(getQtySql);
                if (dtQty.Rows.Count == 0)
                {
                    result.code = 500;
                    result.message = "错误，当前产品未维护装箱数量";
                    return result;
                }
                // 一个外箱最多装几个内盒
                boxQty = int.Parse(dtQty.Rows[0][0].ToString());
                // 查出内盒库中所有符合条件的内盒
                string param = $"T2.LOT_ID = '{shipInsp.subLotId}'";
                inboxSql = inboxSql.Replace("?", param);
                System.Data.DataTable dtBox = SqlUtils.SelectData(inboxSql);

                if (dtBox.Rows.Count == 0)
                {
                    result.code = 500;
                    result.message = "当前内盒库中没有符合条件的内盒";
                    return result;
                }

                // 通过并向规则过滤出所有能用的内盒
                DataTable dtBoxFifer = FifterByRuleToOther(dtBox, boxQty, shipInsp);
                if (dtBoxFifer == null)
                {
                    result.code = 500;
                    result.message = "当前内盒库中没有符合条件的内盒";
                    return result;

                }
                dtBox = dtBoxFifer;
                result.code = 200;
                result.data = dtBox;
                result.message = "发送成功";
                return result;
            }
            catch (Exception ex)
            {
                Result result = new Result();
                result.code = 500;
                result.message = "系统错误";
                return result;
            }
        }
        #endregion

        #region 指令类型 5 指定内盒号
        private Result GenerateByInBoxId(ReceiveInsp shipInsp)
        {
            Result result = new Result();
            string boxId = shipInsp.boxId[0];
            string getQtySql = $"SELECT T2.STANDARD_QTY FROM ( SELECT BOX_ID , MAT_ID  FROM MESMGR.CTAPOBXSTS GROUP BY MAT_ID , BOX_ID ) T1 LEFT JOIN MESMGR.MTAPMATLBL T2 ON T1.MAT_ID = T2.MAT_ID WHERE T1.BOX_ID = '{boxId}' AND T2.OUTBOX_FLAG = 'Y' GROUP BY T2.STANDARD_QTY  ";
            System.Data.DataTable dtQty = SqlUtils.SelectData(getQtySql);
            if (dtQty.Rows.Count == 0)
            {
                result.code = 500;
                result.message = "错误，当前产品未维护装箱数量";
                return result;
            }
            // 一个外箱最多装几个内盒
            boxQty = int.Parse(dtQty.Rows[0][0].ToString());
            // 查出内盒库中所有符合条件的内盒
            string param = $"T2.BOX_ID IN ({shipInsp.GetBoxIdForSql().Trim()})";
            inboxSql = inboxSql.Replace("?", param);
            System.Data.DataTable dtBox = SqlUtils.SelectData(inboxSql);

            if (dtBox.Rows.Count == 0)
            {
                result.code = 500;
                result.message = "当前内盒库中没有符合条件的内盒";
                return result;
            }

            // 通过并向规则过滤出所有能用的内盒
            DataTable dtBoxFifer = FifterByRuleToOther(dtBox, boxQty, shipInsp);
            if (dtBoxFifer == null)
            {
                result.code = 500;
                result.message = "当前内盒库中没有符合条件的内盒";
                return result;

            }
            dtBox = dtBoxFifer;
            result.code = 200;
            result.data = dtBox;
            result.message = "发送成功";
            return result;
        }
        #endregion

        #region 获取并箱规则并过滤

        #region 型号1 判定数量的过滤
        public DataTable FifterByRule(DataTable trsInboxInfo, int boxInQty, ReceiveInsp shipInsp)
        {
            DataTable trsInboxs = new DataTable(); // 能放进去的
            if (trsInboxs.Columns.Count == 0)
            {
                trsInboxs.Columns.Add("LOT_ID");
                trsInboxs.Columns.Add("SHELF_ID");
                trsInboxs.Columns.Add("BOX_ID");
                trsInboxs.Columns.Add("PO_NO");
                trsInboxs.Columns.Add("MO_NO");
                trsInboxs.Columns.Add("BOX_QTY");
                trsInboxs.Columns.Add("IS_FULL");
                trsInboxs.Columns.Add("CUST_LOT_ID");
                trsInboxs.Columns.Add("PO_TIME");
                trsInboxs.Columns.Add("DEVICE");
                trsInboxs.Columns.Add("PID");
                trsInboxs.Columns.Add("LOT_ID2");
                trsInboxs.Columns.Add("IS_REEL_FULL");
            }
            DataTable cacheInboxs = new DataTable();  // 暂存箱
            if (cacheInboxs.Columns.Count == 0)
            {
                cacheInboxs.Columns.Add("LOT_ID");
                cacheInboxs.Columns.Add("SHELF_ID");
                cacheInboxs.Columns.Add("BOX_ID");
                cacheInboxs.Columns.Add("PO_NO");
                cacheInboxs.Columns.Add("MO_NO");
                cacheInboxs.Columns.Add("BOX_QTY");
                cacheInboxs.Columns.Add("IS_FULL");
                cacheInboxs.Columns.Add("CUST_LOT_ID");
                cacheInboxs.Columns.Add("PO_TIME");
                cacheInboxs.Columns.Add("DEVICE");
                cacheInboxs.Columns.Add("PID");
                cacheInboxs.Columns.Add("LOT_ID2");
                cacheInboxs.Columns.Add("IS_REEL_FULL");
            }
            shipInsp.consumerId = shipInsp.type == 1 ? shipInsp.consumerId : GetCustId(shipInsp);
            if (shipInsp.consumerId == null || shipInsp.consumerId.Equals(""))
            {
                return null;
            }
            #region 2025011710 注释 
            /*
            // 1. 先根据型号查
            mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{shipInsp.prodModel}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='DEVICE' ORDER BY RULE_TYPE DESC");

            //2.型号查不出来再获取该客户的并箱规则
            if (mergeBoxRules == null || mergeBoxRules.Rows.Count == 0)
            {
                mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{shipInsp.consumerId}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='CUSTOM' ORDER BY RULE_TYPE DESC");
            }

            if (mergeBoxRules.Rows.Count == 0)
            {
                //  MPCF.ShowMsgBox("该客户：" + shipInsp.consumerId + "没有维护并箱规则，请联系PE维护并箱规则！！！");
                return null;
            }
            if ( mergeBoxRules.Rows[mergeBoxRules.Rows.Count -1]["RULE_TYPE"].ToString().Equals("数量"))
            {
                flagRuleType = true;
            }
            */
            #endregion

            int count = 0; // 计数器 用于外箱放满时清空
            int noFullBoxQty = 0;  // 计数器 用于判断一个外箱中，是否有一个或以上不满数的内盒
            int noReelFullBoxQty = 0;// 计数器 用于判断一个外箱中，是否有一个或以上不满卷的内盒
            bool noFullFlag = false; // 用于放满箱后数量超了，之后的满箱直接continue
            // 遍历每个箱子
            foreach (DataRow row in trsInboxInfo.Rows)
            {
                if (noFullFlag && row["IS_FULL"].ToString().Equals("Y"))
                {
                    continue;
                }
                #region 2025011710  添加 获取每个箱子的并箱规则
                // 1.根据LOT查
                mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{row["LOT_ID"].ToString()}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='LOT' ORDER BY RULE_TYPE DESC");
                // 2.根据PO查
                if(mergeBoxRules == null || mergeBoxRules.Rows.Count == 0)
                {
                    mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{row["PO_NO"].ToString()}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='PO' ORDER BY RULE_TYPE DESC");
                }
                // 3.根据型号查
                if (mergeBoxRules == null || mergeBoxRules.Rows.Count == 0)
                {
                    mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{row["LOT_CMF_5"].ToString()}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='DEVICE' ORDER BY RULE_TYPE DESC");
                }
                //4.型号查不出来再获取该客户的并箱规则
                if (mergeBoxRules == null || mergeBoxRules.Rows.Count == 0)
                {
                    mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{shipInsp.consumerId}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='CUSTOM' ORDER BY RULE_TYPE DESC");
                }
                if (mergeBoxRules.Rows.Count == 0)
                {
                    //  MPCF.ShowMsgBox("该客户：" + shipInsp.consumerId + "没有维护并箱规则，请联系PE维护并箱规则！！！");
                    return null;
                }
                if (mergeBoxRules.Rows[mergeBoxRules.Rows.Count - 1]["RULE_TYPE"].ToString().Equals("数量"))
                {
                    flagRuleType = true;
                }
                #endregion

                if (flagRuleType && !lotPoInfo.ContainsKey(row["LOT_ID2"].ToString()))
                {
                    DataTable dtMergeLots = new DataTable();
                    if (dtMergeLots.Columns.Count == 0)
                    {
                        dtMergeLots.Columns.Add("LOT_ID");
                        dtMergeLots.Columns.Add("PO_NO");
                    }
                    string[] boxLots = row["LOT_ID2"].ToString().Split(",");
                    foreach (string lot in boxLots)
                    {
                        DataRow dr = dtMergeLots.NewRow();
                        dr["LOT_ID"] = lot;
                        DataTable dtPo = SqlUtils.SelectData($"SELECT ORDER_ID  FROM MESMGR.MWIPLOTSTS WHERE LOT_ID = '{lot}'");
                        dr["PO_NO"] = dtPo.Rows[0]["ORDER_ID"].ToString();
                        dtMergeLots.Rows.Add(dr);
                        GetFromIdByLot(lot,dtMergeLots);      
                    }
                    lotPoInfo.Add(row["LOT_ID2"].ToString(), dtMergeLots);
                }
                // 如果通过规则，则添加
                if (MergeBoxFor(row, shipInsp))
                {
                    // 第一个能装进去的为首箱
                    if (firstBox == null)
                    {
                        firstBox = row;
                    }
                    currentQty += int.Parse(row["BOX_QTY"].ToString());
                    // 加起来大于出货数量了，跳过这个箱子
                    if (currentQty > shipInsp.qty)
                    {
                        maxQty = currentQty;
                        currentQty -= int.Parse(row["BOX_QTY"].ToString());
                        noFullFlag = true;
                        continue;
                    }
                    trsInboxs.Rows.Add(row.ItemArray);
                    // 记录不满数量盒子 Index
                    if (row["IS_FULL"].Equals("N"))
                    {
                        // noFullIndex.Add(trsInboxs.Rows.Count - 1);
                        noFullBoxQty++;
                    }
                    // 记录不满卷盒子 Index
                    if (row["IS_REEL_FULL"].Equals("N"))
                    {
                        noReelFullBoxQty++;
                        // noReelFullIndex.Add(trsInboxs.Rows.Count - 1);
                    }
                    //数量等于了出货数量
                    if (currentQty == shipInsp.qty)
                    {
                        return trsInboxs;
                    }
                    count++;
                    if (count == boxInQty) // 装满一箱了
                    {
                        // 一箱装完,查看是否 ,判断满数
                        if (shipMethod && noFullBoxQty > 0)
                        { // 有不满盒（数量） ，暂时放入尾箱
                            int i = trsInboxs.Rows.Count - boxInQty;
                            DataTable deprBox = new DataTable();
                            if (deprBox.Columns.Count == 0)
                            {
                                deprBox.Columns.Add("LOT_ID");
                                deprBox.Columns.Add("SHELF_ID");
                                deprBox.Columns.Add("BOX_ID");
                                deprBox.Columns.Add("PO_NO");
                                deprBox.Columns.Add("MO_NO");
                                deprBox.Columns.Add("BOX_QTY");
                                deprBox.Columns.Add("IS_FULL");
                                deprBox.Columns.Add("CUST_LOT_ID");
                                deprBox.Columns.Add("PO_TIME");
                                deprBox.Columns.Add("DEVICE");
                                deprBox.Columns.Add("PID");
                                deprBox.Columns.Add("LOT_ID2");
                                deprBox.Columns.Add("IS_REEL_FULL");
                            }
                            int length = trsInboxs.Rows.Count;
                            int j = i;
                            int comNum = 0;
                            for (; i < length; i++)
                            {
                                deprBox.Rows.Add(trsInboxs.Rows[j].ItemArray);
                                currentQty -= int.Parse(trsInboxs.Rows[j]["BOX_QTY"].ToString());
                                comNum += int.Parse(trsInboxs.Rows[j]["BOX_QTY"].ToString());
                                trsInboxs.Rows.RemoveAt(j);
                                /*                            noFullIndex.Remove(i);
                                                            noReelFullIndex.Remove(i);*/
                            }
                            // 放入尾箱组合集合
                            deptBoxs.Add(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "," + comNum, deprBox);
                        }

                        if (!shipMethod && noReelFullBoxQty > 0)
                        { // 有不满盒（数量） ，暂时放入尾箱
                            int i = trsInboxs.Rows.Count - boxInQty;
                            DataTable deprBox = new DataTable();
                            if (deprBox.Columns.Count == 0)
                            {
                                deprBox.Columns.Add("LOT_ID");
                                deprBox.Columns.Add("SHELF_ID");
                                deprBox.Columns.Add("BOX_ID");
                                deprBox.Columns.Add("PO_NO");
                                deprBox.Columns.Add("MO_NO");
                                deprBox.Columns.Add("BOX_QTY");
                                deprBox.Columns.Add("IS_FULL");
                                deprBox.Columns.Add("CUST_LOT_ID");
                                deprBox.Columns.Add("PO_TIME");
                                deprBox.Columns.Add("DEVICE");
                                deprBox.Columns.Add("PID");
                                deprBox.Columns.Add("LOT_ID2");
                                deprBox.Columns.Add("IS_REEL_FULL");
                            }
                            int length = trsInboxs.Rows.Count;
                            int j = i;
                            int comNum = 0;
                            for (; i < length; i++)
                            {
                                deprBox.Rows.Add(trsInboxs.Rows[j].ItemArray);
                                currentQty -= int.Parse(trsInboxs.Rows[j]["BOX_QTY"].ToString());
                                comNum += int.Parse(trsInboxs.Rows[j]["BOX_QTY"].ToString());
                                trsInboxs.Rows.RemoveAt(j);
                                /*                            noFullIndex.Remove(i);
                                                            noReelFullIndex.Remove(i);*/
                            }
                            // 放入尾箱组合集合
                            deptBoxs.Add(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "," + comNum, deprBox);
                        }
                        noFullBoxQty = 0;
                        noReelFullBoxQty = 0;
                        count = 0;
                        DataClear();
                    }
                }
                else
                {
                    //装完数量还是不满足，考虑继续加入暂存箱
                    cacheInboxs.Rows.Add(row.ItemArray);
                }

            }
            maxQty = currentQty;
            // 如果数量还是不满足，且暂存箱大于0 ，考虑继续往里面放
            if (currentQty < shipInsp.qty && cacheInboxs.Rows.Count > 0)
            {
                return CacheFifterByRule(trsInboxs, cacheInboxs, boxInQty, cacheInboxs.Rows.Count, shipInsp);
            }
            return null;
        }
        /**
         * alreadyBoxs 已经装好的箱子 
         * cacheBoxs 缓存箱
         * boxInQty 一个外箱装几个内盒
         * cacheBoxQty 数量
         * shipInsp 出货指令
         */
        public DataTable CacheFifterByRule(DataTable alreadyBoxs, DataTable cacheBoxs, int boxInQty, int cacheBoxQty, ReceiveInsp shipInsp)
        {
            firstBox = alreadyBoxs.Rows.Count % boxInQty == 0 ? null : alreadyBoxs.Rows[alreadyBoxs.Rows.Count - alreadyBoxs.Rows.Count % boxInQty];
            // 将最后一个尾箱,回退，放入暂时尾箱的区域，因为与后面的箱子都冲突
            // 有尾箱清除尾箱

            int count = alreadyBoxs.Rows.Count % boxInQty;
            int noFullBoxQty = 0;
            int noReelFullBoxQty = 0;
            DataTable tempData = cacheBoxs.Copy();
            int countCache = 0;
            bool noFullFlag = false;
            for (int a = 0; a < tempData.Rows.Count; a++)
            {
                DataRow row = tempData.Rows[a];
                if (noFullFlag && row["IS_FULL"].ToString().Equals("Y"))
                {
                    continue;
                }
                if (flagRuleType && !lotPoInfo.ContainsKey(row["LOT_ID2"].ToString()))
                {
                    DataTable dtMergeLots = new DataTable();
                    if (dtMergeLots.Columns.Count == 0)
                    {
                        dtMergeLots.Columns.Add("LOT_ID");
                        dtMergeLots.Columns.Add("PO_NO");
                    }
                    string[] boxLots = row["LOT_ID2"].ToString().Split(",");
                    foreach (string lot in boxLots)
                    {
                        DataRow dr = dtMergeLots.NewRow();
                        dr["LOT_ID"] = lot;
                        DataTable dtPo = SqlUtils.SelectData($"SELECT ORDER_ID  FROM MESMGR.MWIPLOTSTS WHERE LOT_ID = '{lot}'");
                        dr["PO_NO"] = dtPo.Rows[0]["ORDER_ID"].ToString();
                        dtMergeLots.Rows.Add(dr);
                        GetFromIdByLot(lot, dtMergeLots);
                    }
                    lotPoInfo.Add(row["LOT_ID2"].ToString(), dtMergeLots);
                }
                if (MergeBoxFor(row, shipInsp))
                {
                    // 第一个能装进去的为首箱
                    if (firstBox == null)
                    {
                        firstBox = row;
                    }
                    currentQty += int.Parse(row["BOX_QTY"].ToString());
                    // 加起来大于出货数量了，跳过这个箱子
                    if (currentQty > shipInsp.qty)
                    { //放进去，数量超了， 把这个箱子从缓存箱子
                        maxQty = currentQty;
                        currentQty -= int.Parse(row["BOX_QTY"].ToString());
                        cacheBoxs.Rows.RemoveAt(countCache);
                        noFullFlag = true;
                        continue;
                    }
                    if (currentQty > maxQty)
                    {
                        maxQty = currentQty;
                    }
                    alreadyBoxs.Rows.Add(row.ItemArray);
                    cacheBoxs.Rows.RemoveAt(countCache);
                    if (row["IS_FULL"].Equals("N"))
                    {
                        // noFullIndex.Add(trsInboxs.Rows.Count - 1);
                        noFullBoxQty++;
                    }
                    // 记录不满卷盒子 Index
                    if (row["IS_REEL_FULL"].Equals("N"))
                    {
                        noReelFullBoxQty++;
                        // noReelFullIndex.Add(trsInboxs.Rows.Count - 1);
                    }
                    //数量等于了出货数量
                    if (currentQty == shipInsp.qty)
                    {
                        return alreadyBoxs;
                    }
                    count++;
                    if (count == boxInQty)
                    {
                        // 一箱装完,查看是否 ,判断满数
                        if (shipMethod && noFullBoxQty > 0)
                        { // 有不满盒（数量） ，暂时放入尾箱
                            int i = alreadyBoxs.Rows.Count - boxInQty;
                            DataTable deprBox = new DataTable();
                            if (deprBox.Columns.Count == 0)
                            {
                                deprBox.Columns.Add("LOT_ID");
                                deprBox.Columns.Add("SHELF_ID");
                                deprBox.Columns.Add("BOX_ID");
                                deprBox.Columns.Add("PO_NO");
                                deprBox.Columns.Add("MO_NO");
                                deprBox.Columns.Add("BOX_QTY");
                                deprBox.Columns.Add("IS_FULL");
                                deprBox.Columns.Add("CUST_LOT_ID");
                                deprBox.Columns.Add("PO_TIME");
                                deprBox.Columns.Add("DEVICE");
                                deprBox.Columns.Add("PID");
                                deprBox.Columns.Add("LOT_ID2");
                                deprBox.Columns.Add("IS_REEL_FULL");
                            }
                            int length = alreadyBoxs.Rows.Count;
                            int j = i;
                            int comNum = 0;
                            for (; i < length; i++)
                            {
                                deprBox.Rows.Add(alreadyBoxs.Rows[j].ItemArray);
                                currentQty -= int.Parse(alreadyBoxs.Rows[j]["BOX_QTY"].ToString());
                                comNum += int.Parse(alreadyBoxs.Rows[j]["BOX_QTY"].ToString());
                                alreadyBoxs.Rows.RemoveAt(j);
                                /*                            noFullIndex.Remove(i);
                                                            noReelFullIndex.Remove(i);*/
                            }
                            // 放入尾箱组合集合
                            deptBoxs.Add(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "," + comNum, deprBox);
                        }

                        if (!shipMethod && noReelFullBoxQty > 0)
                        { // 有不满盒（数量） ，暂时放入尾箱
                            int i = alreadyBoxs.Rows.Count - boxInQty;
                            DataTable deprBox = new DataTable();
                            if (deprBox.Columns.Count == 0)
                            {
                                deprBox.Columns.Add("LOT_ID");
                                deprBox.Columns.Add("SHELF_ID");
                                deprBox.Columns.Add("BOX_ID");
                                deprBox.Columns.Add("PO_NO");
                                deprBox.Columns.Add("MO_NO");
                                deprBox.Columns.Add("BOX_QTY");
                                deprBox.Columns.Add("IS_FULL");
                                deprBox.Columns.Add("CUST_LOT_ID");
                                deprBox.Columns.Add("PO_TIME");
                                deprBox.Columns.Add("DEVICE");
                                deprBox.Columns.Add("PID");
                                deprBox.Columns.Add("LOT_ID2");
                                deprBox.Columns.Add("IS_REEL_FULL");
                            }
                            int length = alreadyBoxs.Rows.Count;
                            int j = i;
                            int comNum = 0;
                            for (; i < length; i++)
                            {
                                deprBox.Rows.Add(alreadyBoxs.Rows[j].ItemArray);
                                currentQty -= int.Parse(alreadyBoxs.Rows[j]["BOX_QTY"].ToString());
                                comNum += int.Parse(alreadyBoxs.Rows[j]["BOX_QTY"].ToString());
                                alreadyBoxs.Rows.RemoveAt(j);
                                /*                            noFullIndex.Remove(i);
                                                            noReelFullIndex.Remove(i);*/
                            }
                            // 放入尾箱组合集合
                            deptBoxs.Add(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "," + comNum, deprBox);
                        }
                        noFullBoxQty = 0;
                        noReelFullBoxQty = 0;
                        count = 0;
                        DataClear();
                    }
                }
                else
                {
                    countCache++;
                }
            }
            // 代表一个也没放进去
            if (countCache == cacheBoxQty)
            {
                // 新开箱子也不满足条件，返回
                if (firstBox == null)
                {
                    return null;
                }
                else
                {
                    // 首箱不为空，回退
                    if (alreadyBoxs.Rows.Count % boxInQty > 0)
                    {
                        int i = alreadyBoxs.Rows.Count - alreadyBoxs.Rows.Count % boxInQty;
                        DataTable deprBox = new DataTable();
                        if (deprBox.Columns.Count == 0)
                        {
                            deprBox.Columns.Add("LOT_ID");
                            deprBox.Columns.Add("SHELF_ID");
                            deprBox.Columns.Add("BOX_ID");
                            deprBox.Columns.Add("PO_NO");
                            deprBox.Columns.Add("MO_NO");
                            deprBox.Columns.Add("BOX_QTY");
                            deprBox.Columns.Add("IS_FULL");
                            deprBox.Columns.Add("CUST_LOT_ID");
                            deprBox.Columns.Add("PO_TIME");
                            deprBox.Columns.Add("DEVICE");
                            deprBox.Columns.Add("PID");
                            deprBox.Columns.Add("LOT_ID2");
                            deprBox.Columns.Add("IS_REEL_FULL");
                        }
                        int length = alreadyBoxs.Rows.Count;
                        int j = i;
                        maxQty = currentQty;
                        int comNum = 0;
                        for (; i < length; i++)
                        {
                            deprBox.Rows.Add(alreadyBoxs.Rows[j].ItemArray);
                            currentQty -= int.Parse(alreadyBoxs.Rows[j]["BOX_QTY"].ToString());
                            comNum += int.Parse(alreadyBoxs.Rows[j]["BOX_QTY"].ToString());
                            alreadyBoxs.Rows.RemoveAt(j);
                            /*                            noFullIndex.Remove(i);
                                                        noReelFullIndex.Remove(i);*/
                        }
                        // 放入尾箱组合集合
                        deptBoxs.Add(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "," + comNum, deprBox);
                        DataClear();
                    }
                    return CacheFifterByRule(alreadyBoxs, cacheBoxs, boxInQty, cacheBoxs.Rows.Count, shipInsp);
                }
            }
            if (cacheBoxs.Rows.Count == 0)
            {
                //判断此时数量是否正确
                if (currentQty == shipInsp.qty)
                {
                    return alreadyBoxs;
                }
                else
                {
                    //如果有尾箱，先把现有尾箱子去掉
                    // 将最后一个尾箱,回
                    int b = alreadyBoxs.Rows.Count - alreadyBoxs.Rows.Count % boxInQty;
                    int lengthNull = alreadyBoxs.Rows.Count;
                    int x = b;
                    maxQty = currentQty;
                    for (; b < lengthNull; b++)
                    {
                        currentQty -= int.Parse(alreadyBoxs.Rows[x]["BOX_QTY"].ToString());
                        alreadyBoxs.Rows.RemoveAt(x);
                    }
                    int tempQty = currentQty;
                    // 记录此时数量
                    int aIndex = alreadyBoxs.Rows.Count;
                    DataTable tempAlreadyBoxs = alreadyBoxs.Copy();
                    // 还是不满足，清除 尾箱，将尾箱箱中的所有组合再加一遍，不行就 return null
                    DataTable rightDt = new DataTable();
                    foreach (string key in deptBoxs.Keys)
                    {
                        int boxQty = int.Parse(key.Split(',')[1]);
                        if (maxQty < currentQty + boxQty)
                        {
                            maxQty = currentQty + boxQty;
                        }
                        if (currentQty + boxQty == shipInsp.qty)
                        {
                            rightDt = deptBoxs[key];
                            break;
                        }
                    }

                    if (rightDt != null && rightDt.Rows.Count > 0)
                    {
                        foreach (DataRow row in rightDt.Rows)
                        {
                            alreadyBoxs.Rows.Add(row.ItemArray);
                            //数量等于了出货数量
                        }
                        return alreadyBoxs;
                    }
                    return null;
                }

            }

            //装完数量还是不满足，考虑继续加入暂存箱
            if (currentQty < shipInsp.qty)
            {
                return CacheFifterByRule(alreadyBoxs, cacheBoxs, boxInQty, cacheBoxs.Rows.Count, shipInsp);
            }
            return null;
            // 如果下一次过滤还有这么多不满箱子，则返回

        }
        #endregion

        #region  除了型号1之外的 过滤
        public DataTable FifterByRuleToOther(DataTable trsInboxInfo, int boxInQty, ReceiveInsp shipInsp)
        {
            DataTable trsInboxs = new DataTable();
            if (trsInboxs.Columns.Count == 0)
            {
                trsInboxs.Columns.Add("LOT_ID");
                trsInboxs.Columns.Add("SHELF_ID");
                trsInboxs.Columns.Add("BOX_ID");
                trsInboxs.Columns.Add("PO_NO");
                trsInboxs.Columns.Add("MO_NO");
                trsInboxs.Columns.Add("BOX_QTY");
                trsInboxs.Columns.Add("IS_FULL");
                trsInboxs.Columns.Add("CUST_LOT_ID");
                trsInboxs.Columns.Add("PO_TIME");
                trsInboxs.Columns.Add("DEVICE");
                trsInboxs.Columns.Add("PID");
                trsInboxs.Columns.Add("LOT_ID2");
                trsInboxs.Columns.Add("IS_REEL_FULL");
            }
            DataTable cacheInboxs = new DataTable();
            if (cacheInboxs.Columns.Count == 0)
            {
                cacheInboxs.Columns.Add("LOT_ID");
                cacheInboxs.Columns.Add("SHELF_ID");
                cacheInboxs.Columns.Add("BOX_ID");
                cacheInboxs.Columns.Add("PO_NO");
                cacheInboxs.Columns.Add("MO_NO");
                cacheInboxs.Columns.Add("BOX_QTY");
                cacheInboxs.Columns.Add("IS_FULL");
                cacheInboxs.Columns.Add("CUST_LOT_ID");
                cacheInboxs.Columns.Add("PO_TIME");
                cacheInboxs.Columns.Add("DEVICE");
                cacheInboxs.Columns.Add("PID");
                cacheInboxs.Columns.Add("LOT_ID2");
                cacheInboxs.Columns.Add("IS_REEL_FULL");
            }
            #region 2025011716 注释 
            /*
            switch (shipInsp.type)
            {
                case 2:
                    mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{shipInsp.poNo}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='MO' ORDER BY RULE_TYPE DESC");
                    break;
                case 3:
                    //2025011611增加 mo的过滤
                    mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{shipInsp.moNo}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='PO' ORDER BY RULE_TYPE DESC");
                    break;
                case 4:
                    mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{shipInsp.subLotId}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='LOT' ORDER BY RULE_TYPE DESC");
                    break;
                case 5:
                    //2025011611 增加box 的过滤
                    mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{shipInsp.moNo}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='BOX' ORDER BY RULE_TYPE DESC");
                    break;
                default:
                    break;
            }

            if (mergeBoxRules == null || mergeBoxRules.Rows.Count == 0)
            {
                //shipInsp.consumerId = shipInsp.consumerId == null ? shipInsp.consumerId : GetCustId(shipInsp);
                shipInsp.consumerId = shipInsp.consumerId == null ? GetCustId(shipInsp):shipInsp.consumerId; //2025011611修复
                if (shipInsp.consumerId == null || shipInsp.consumerId.Equals(""))
                {
                    return null;
                }
                //获取该客户的并箱规则
                mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{shipInsp.consumerId}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='CUSTOM' ORDER BY RULE_TYPE DESC");
            }
            if (mergeBoxRules.Rows.Count == 0)
            {
                return null;
            }
            if (mergeBoxRules.Rows[mergeBoxRules.Rows.Count - 1]["RULE_TYPE"].ToString().Equals("数量"))
            {
                flagRuleType = true;
            }
            */
            #endregion

            int count = 0;
            // 遍历每个箱子
            foreach (DataRow row in trsInboxInfo.Rows)
            {
                #region 2025011716  添加 获取每个箱子的并箱规则
                // 1.根据LOT查
                mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{row["LOT_ID"].ToString()}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='LOT' ORDER BY RULE_TYPE DESC");
                // 2.根据PO查
                if (mergeBoxRules == null || mergeBoxRules.Rows.Count == 0)
                {
                    mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{row["PO_NO"].ToString()}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='PO' ORDER BY RULE_TYPE DESC");
                }
                // 3.根据型号查
                if (mergeBoxRules == null || mergeBoxRules.Rows.Count == 0)
                {
                    mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{row["LOT_CMF_5"].ToString()}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='DEVICE' ORDER BY RULE_TYPE DESC");
                }

                //4.型号查不出来再获取该客户的并箱规则
                if (mergeBoxRules == null || mergeBoxRules.Rows.Count == 0)
                {
                    mergeBoxRules = SqlUtils.SelectData($"SELECT T2.RULE_ID ,T1.STEP ,T2.CUSTOMIZATION ,T2.DATA_SOURCE ,T2.RULE_TYPE ,T2.SYMBOL ,T1.VALUE  FROM MESMGR.MERGEBOXRULE T1 LEFT JOIN MESMGR.MERGEBOXRULESETTING T2 ON T1.RULE_ID  = T2.RULE_ID  WHERE MAP_ID = '{shipInsp.consumerId}' AND t1.STEP LIKE '%Carton%' AND T1.TYPE='CUSTOM' ORDER BY RULE_TYPE DESC");
                }

                if (mergeBoxRules.Rows.Count == 0)
                {
                    //  MPCF.ShowMsgBox("该客户：" + shipInsp.consumerId + "没有维护并箱规则，请联系PE维护并箱规则！！！");
                    return null;
                }
                if (mergeBoxRules.Rows[mergeBoxRules.Rows.Count - 1]["RULE_TYPE"].ToString().Equals("数量"))
                {
                    flagRuleType = true;
                }
                #endregion

                if (flagRuleType && !lotPoInfo.ContainsKey(row["LOT_ID2"].ToString()))
                {
                    DataTable dtMergeLots = new DataTable();
                    if (dtMergeLots.Columns.Count == 0)
                    {
                        dtMergeLots.Columns.Add("LOT_ID");
                        dtMergeLots.Columns.Add("PO_NO");
                    }
                    string[] boxLots = row["LOT_ID2"].ToString().Split(",");
                    foreach (string lot in boxLots)
                    {
                        DataRow dr = dtMergeLots.NewRow();
                        dr["LOT_ID"] = lot;
                        DataTable dtPo = SqlUtils.SelectData($"SELECT ORDER_ID  FROM MESMGR.MWIPLOTSTS WHERE LOT_ID = '{lot}'");
                        dr["PO_NO"] = dtPo.Rows[0]["ORDER_ID"].ToString();
                        dtMergeLots.Rows.Add(dr);
                        GetFromIdByLot(lot, dtMergeLots);
                    }
                    lotPoInfo.Add(row["LOT_ID2"].ToString(), dtMergeLots);
                }
                // 如果通过规则，则添加
                if (MergeBoxFor(row, shipInsp))
                {
                    // 第一个能装进去的为首箱
                    if (firstBox == null)
                    {
                        firstBox = row;
                    }
                    trsInboxs.Rows.Add(row.ItemArray);
                    //数量等于了出货数量
                    count++;
                    if (count == boxInQty)
                    {
                        count = 0;
                        DataClear();
                    }
                }
                else
                {
                    cacheInboxs.Rows.Add(row.ItemArray);
                }

            }
            return cacheInboxs.Rows.Count == 0 ? trsInboxs : CacheFifterByRuleToOther(trsInboxs, cacheInboxs, boxInQty, cacheInboxs.Rows.Count, shipInsp); ;
        }

        public DataTable CacheFifterByRuleToOther(DataTable alreadyBoxs, DataTable cacheBoxs, int boxInQty, int cacheBoxQty, ReceiveInsp shipInsp)
        {

            // 将最后一个尾箱,回退，放入暂时尾箱的区域，因为与后面的箱子都冲突
            if (alreadyBoxs.Rows.Count % boxInQty > 0)
            {
                int i = alreadyBoxs.Rows.Count - alreadyBoxs.Rows.Count % boxInQty;
                DataTable deprBox = new DataTable();
                if (deprBox.Columns.Count == 0)
                {
                    deprBox.Columns.Add("LOT_ID");
                    deprBox.Columns.Add("SHELF_ID");
                    deprBox.Columns.Add("BOX_ID");
                    deprBox.Columns.Add("PO_NO");
                    deprBox.Columns.Add("MO_NO");
                    deprBox.Columns.Add("BOX_QTY");
                    deprBox.Columns.Add("IS_FULL");
                    deprBox.Columns.Add("CUST_LOT_ID");
                    deprBox.Columns.Add("PO_TIME");
                    deprBox.Columns.Add("DEVICE");
                    deprBox.Columns.Add("PID");
                    deprBox.Columns.Add("LOT_ID2");
                    deprBox.Columns.Add("IS_REEL_FULL");
                }
                int length = alreadyBoxs.Rows.Count;
                int j = i;
                for (; i < length; i++)
                {
                    deprBox.Rows.Add(alreadyBoxs.Rows[j].ItemArray);
                    alreadyBoxs.Rows.RemoveAt(j);
                }
                // 放入尾箱组合集合
                deptBoxs.Add(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "", deprBox);
                DataClear();
            }

            //已经装好箱子的最后一个箱子的首箱
            // firstBox = alreadyBoxs.Rows.Count % boxInQty == 0 ? null : alreadyBoxs.Rows[alreadyBoxs.Rows.Count - alreadyBoxs.Rows.Count % boxInQty];
            int count = alreadyBoxs.Rows.Count % boxInQty;
            DataTable tempData = cacheBoxs.Copy();
            int countCache = 0;
            for (int a = 0; a < tempData.Rows.Count; a++)
            {
                DataRow row = tempData.Rows[a];
                if (flagRuleType && !lotPoInfo.ContainsKey(row["LOT_ID2"].ToString()))
                {
                    DataTable dtMergeLots = new DataTable();
                    if (dtMergeLots.Columns.Count == 0)
                    {
                        dtMergeLots.Columns.Add("LOT_ID");
                        dtMergeLots.Columns.Add("PO_NO");
                    }
                    string[] boxLots = row["LOT_ID2"].ToString().Split(",");
                    foreach (string lot in boxLots)
                    {
                        DataRow dr = dtMergeLots.NewRow();
                        dr["LOT_ID"] = lot;
                        DataTable dtPo = SqlUtils.SelectData($"SELECT ORDER_ID  FROM MESMGR.MWIPLOTSTS WHERE LOT_ID = '{lot}'");
                        dr["PO_NO"] = dtPo.Rows[0]["ORDER_ID"].ToString();
                        dtMergeLots.Rows.Add(dr);
                        GetFromIdByLot(lot, dtMergeLots);
                    }
                    lotPoInfo.Add(row["LOT_ID2"].ToString(), dtMergeLots);
                }
                if (MergeBoxFor(row, shipInsp))
                {
                    // 第一个能装进去的为首箱
                    if (firstBox == null)
                    {
                        firstBox = row;
                    }
                    /*                    currentQty += int.Parse(row["BOX_QTY"].ToString());
                                        // 加起来大于出货数量了，跳过这个箱子
                                        if (currentQty > shipInsp.qty)
                                        {
                                            currentQty -= int.Parse(row["BOX_QTY"].ToString());
                                            continue;
                                        }*/
                    alreadyBoxs.Rows.Add(row.ItemArray);
                    cacheBoxs.Rows.RemoveAt(countCache);
                    /*                    //数量等于了出货数量
                                        if (currentQty == shipInsp.qty)
                                        {
                                            return alreadyBoxs;
                                        }*/
                    count++;
                    if (count == boxInQty)
                    {
                        count = 0;
                        DataClear();
                    }
                }
                else
                {
                    countCache++;
                }
            }


            if (countCache == cacheBoxQty)
            {
                // 填充成满箱后，从暂弃箱中获取并填充
                foreach (DataTable dt in deptBoxs.Values)
                {
                    // 填充空箱数量
                    int nullBoxLength = boxInQty - dt.Rows.Count;
                    foreach (DataRow row in dt.Rows)
                    {
                        alreadyBoxs.Rows.Add(row.ItemArray);
                    }
                    for (int J = 0; J < nullBoxLength; J++)
                    {
                        DataRow dr = alreadyBoxs.NewRow();
                        dr["BOX_ID"] = "空箱";
                        alreadyBoxs.Rows.Add(dr.ItemArray);
                    }
                }

                return alreadyBoxs;
            }
            // 过滤之后隐藏箱子没变，代表放不进去了，直接返回
            if (cacheBoxs.Rows.Count == 0)
            {
                // 遍历所有组合并填充空箱子出货
                //判断是否为满箱,为不满箱填充空箱子
                if (alreadyBoxs.Rows.Count % boxInQty != 0)
                {
                    // 填充空巷
                    int lengthNull = boxInQty - (alreadyBoxs.Rows.Count % boxInQty);
                    for (int b = 0; b < lengthNull; b++)
                    {
                        // 填充空箱
                        DataRow dr = alreadyBoxs.NewRow();
                        dr["BOX_ID"] = "空箱";
                        alreadyBoxs.Rows.Add(dr.ItemArray);
                    }
                }
                // 填充成满箱后，从暂弃箱中获取并填充
                foreach (DataTable dt in deptBoxs.Values)
                {
                    // 填充空箱数量
                    int nullBoxLength = boxInQty - dt.Rows.Count;
                    foreach (DataRow row in dt.Rows)
                    {
                        alreadyBoxs.Rows.Add(row.ItemArray);
                    }
                    for (int J = 0; J < nullBoxLength; J++)
                    {
                        DataRow dr = alreadyBoxs.NewRow();
                        dr["BOX_ID"] = "空箱";
                        alreadyBoxs.Rows.Add(dr.ItemArray);
                    }
                }




                return alreadyBoxs;
            }
            return CacheFifterByRuleToOther(alreadyBoxs, cacheBoxs, boxInQty, cacheBoxs.Rows.Count, shipInsp);
            // 如果下一次过滤还有这么多不满箱子，则返回

        }
        #endregion

        #region 过滤
        public bool MergeBoxFor(DataRow addBox, ReceiveInsp shipInsp)
        {
            foreach (DataRow rule in mergeBoxRules.Rows) {
                if (!MergeBoxCheck(rule, addBox, shipInsp))
                {
                    return false;
                }
            }
            return true;
        }
        public bool MergeBoxCheck(DataRow mergeRule, DataRow addBox, ReceiveInsp shipInsp)
        {
            string ruleId = mergeRule[0].ToString();
            string target = mergeRule[1].ToString();
            string isCust = mergeRule[2].ToString();
            string dataSource = mergeRule[3].ToString();
            string ruleTpe = mergeRule[4].ToString();
            string symbol = mergeRule[5].ToString();
            string value = mergeRule[6].ToString();
            int max = 0;
            if (value != null && !value.Equals("/"))
            {
                string[] strV = value.Split("/");
                max = int.Parse(strV[strV.Length - 1]);
            }
            switch (ruleId)
            {
                case "SCI":
                    return SameCustomId(addBox[0].ToString());
                    break;
                case "SPN":
                    return SameDevice(addBox[0].ToString());
                    break;
                case "SPK":
                    return SamePackage(addBox[0].ToString());
                    break;
                case "SBG":
                    return SameBinGrade(addBox[0].ToString(), shipInsp);
                    break;
                case "SPD":
                    return SamePID(addBox[0].ToString());
                    break;
                case "SL1":
                    return SameLblType1(addBox[3].ToString());
                    break;
                case "SL2":
                    return SameLblType2(addBox[3].ToString());
                    break;
                case "SW1":
                    return SameWaferName1(addBox[3].ToString());
                    break;
                case "SW2":
                    return SameWaferName2(addBox[3].ToString());
                    break;
                case "SWV":
                    return SameWaferVerison(addBox[3].ToString());
                    break;
                case "MRO":
                    return SameMarkOneRow(addBox[3].ToString());
                    break;
                case "MMC":
                    return MaxMarkValue(max,addBox);
                    break;
                case "MWL":
                    return MaxWaferIdValue(max, addBox);
                    break;
                case "MD1":
                    return MaxDCValue1(max, addBox);
                    break;
                case "MD2":
                    return MaxDCValue2(max, addBox);
                    break;
                case "MLQ":
                    return MaxLotQtyValue(max, addBox);
                    break;
                case "SBD":
                    return SameBDDaring(addBox[3].ToString());
                    break;
                case "SMN":
                    return SameMarkRule(addBox[3].ToString());
                    break;
                case "STP":
                    return SameTestProgram(addBox[0].ToString());
                    break;
                case "SLT":
                    return SameLotType(addBox[0].ToString());
                    break;
            }

            return true;
        }
        #endregion

        #region 并箱规则

        #region 相同的客户号
        private bool SameCustomId(string addLotId)
        {
            if (firstBox != null)
            {
                string fristLotId = firstBox[0].ToString();
                if (fristLotId.Equals(addLotId))
                {
                    return true;
                }
                string sql = $"SELECT CASE   WHEN T1.LOT_CMF_2 = T2.LOT_CMF_2  THEN 'Y'  ELSE 'N'  END AS IS_EQUAL FROM ( SELECT  LOT_CMF_2 FROM  MESMGR.MWIPLOTSTS WHERE  LOT_ID = '{fristLotId}') T1 ,( SELECT  LOT_CMF_2 FROM  MESMGR.MWIPLOTSTS WHERE  LOT_ID = '{addLotId}') T2";
                DataTable dtCust = SqlUtils.SelectData(sql);
                if (dtCust.Rows.Count == 0 || dtCust.Rows[0][0].ToString().Equals("N"))
                {
                    return false;
                }
                return true;
            }
            return true;
        }
        #endregion

        #region 相同产品号
        private bool SameDevice(string addLotId)
        {
            if (firstBox != null)
            {
                string fristLotId = firstBox[0].ToString();
                if (fristLotId.Equals(addLotId))
                {
                    return true;
                }
                string sql = $"SELECT CASE   WHEN T1.LOT_CMF_5 = T2.LOT_CMF_5  THEN 'Y'  ELSE 'N'  END AS IS_EQUAL FROM ( SELECT  LOT_CMF_5 FROM  MESMGR.MWIPLOTSTS WHERE  LOT_ID = '{fristLotId}') T1 ,( SELECT  LOT_CMF_5 FROM  MESMGR.MWIPLOTSTS WHERE  LOT_ID = '{addLotId}') T2";
                DataTable dtProdModel = SqlUtils.SelectData(sql);
                if (dtProdModel.Rows.Count == 0 || dtProdModel.Rows[0][0].ToString().Equals("N"))
                {
                    return false;
                }
                return true;
            }
            return true;
        }
        #endregion

        #region 相同的Package
        private bool SamePackage(string addLotId)
        {
            // 和首箱比较
            if (firstBox != null)
            {
                string fristLotId = firstBox[0].ToString();
                if (fristLotId.Equals(addLotId))
                {
                    return true;
                }
                string sql = $"SELECT CASE WHEN T1.LOT_CMF_7 = T2.LOT_CMF_7  THEN 'Y'  ELSE 'N'  END AS IS_EQUAL FROM ( SELECT  LOT_CMF_7 FROM  MESMGR.MWIPLOTSTS WHERE  LOT_ID = '{fristLotId}') T1 ,( SELECT  LOT_CMF_7 FROM  MESMGR.MWIPLOTSTS WHERE  LOT_ID = '{addLotId}') T2";
                DataTable dtPackage = SqlUtils.SelectData(sql);
                if (dtPackage.Rows.Count == 0 || dtPackage.Rows[0][0].ToString().Equals("N"))
                {
                    return false;
                }
                return true;
            }
            return true;
        }
        #endregion

        #region 相同的BIN别
        private bool SameBinGrade(string addLotId, ReceiveInsp shipInsp)
        {
            if (firstBox != null)
            {
                string fristLotId = firstBox[0].ToString();
                if (fristLotId.Equals(addLotId))
                {
                    return true;
                }
                string sql = $"SELECT  MAT_ID FROM  MESMGR.MWIPLOTSTS WHERE  LOT_ID = '{fristLotId}'";
                DataTable dt1 = SqlUtils.SelectData(sql);
                string matIdF = dt1.Rows[0][0].ToString();
                string sql2 = $"SELECT  MAT_ID FROM  MESMGR.MWIPLOTSTS WHERE  LOT_ID = '{addLotId}'";
                DataTable dt2 = SqlUtils.SelectData(sql2);
                string matIdA = dt2.Rows[0][0].ToString();

                string binTypeF = GetBinGradeByLotId(fristLotId, shipInsp.consumerId, matIdF, true);
                string binTypeA = GetBinGradeByLotId(addLotId, shipInsp.consumerId, matIdA, true);
                if (!binTypeF.Equals(binTypeA))
                {
                    return false;
                }
                return true;
            }
            return true;
        }
        #endregion

        #region 相同的PID
        private bool SamePID(string addLotId)
        {
            if (firstBox != null)
            {
                string fristLotId = firstBox[0].ToString();
                if (fristLotId.Equals(addLotId))
                {
                    return true;
                }
                string sql = $"SELECT CASE WHEN T1.MAT_ID = T2.MAT_ID  THEN 'Y'  ELSE 'N'  END AS IS_EQUAL FROM ( SELECT  MAT_ID FROM  MESMGR.MWIPLOTSTS WHERE  LOT_ID = '{fristLotId}') T1 ,( SELECT  MAT_ID FROM  MESMGR.MWIPLOTSTS WHERE  LOT_ID = '{addLotId}') T2";
                DataTable dtPid = SqlUtils.SelectData(sql);
                if (dtPid.Rows.Count == 0 || dtPid.Rows[0][0].ToString().Equals("N"))
                {
                    return false;
                }
                return true;
            }
            return true;
        }
        #endregion

        #region 相同的标签型号（客户工单中的标签型号）
        private bool SameLblType1(string addPoNo)
        {
            if (firstBox != null)
            {
                string fristPoNo = firstBox[3].ToString();
                if (fristPoNo.Equals(addPoNo))
                {
                    return true;
                }
                string sql = $"SELECT CASE WHEN T1.PO_CMF_18 = T2.PO_CMF_18 THEN 'Y' ELSE 'N' END AS IS_EQUAL FROM  (SELECT PO_CMF_18 FROM MESMGR.MTAPCPOSTS GROUP BY PO_NO,PO_CMF_18  HAVING PO_NO = '{fristPoNo}') T1, (SELECT PO_CMF_18 FROM MESMGR.MTAPCPOSTS GROUP BY PO_NO,PO_CMF_18  HAVING PO_NO = '{addPoNo}') T2";
                DataTable dtLabel = SqlUtils.SelectData(sql);
                if (dtLabel.Rows.Count == 0 || dtLabel.Rows[0][0].ToString().Equals("N"))
                {
                    return false;
                }
                return true;
            }
            return true;
        }
        #endregion

        #region 相同的标签型号（客户标签 Product Part No 相同）
        private bool SameLblType2(string addPoNo)
        {
            if (firstBox != null)
            {
                string fristPoNo = firstBox[3].ToString();
                if (fristPoNo.Equals(addPoNo))
                {
                    return true;
                }
                string sql = $"SELECT CASE WHEN T1.PO_CMF_18 = T2.PO_CMF_18 THEN 'Y' ELSE 'N' END AS IS_EQUAL FROM  (SELECT PO_CMF_18 FROM MESMGR.MTAPCPOSTS GROUP BY PO_NO,PO_CMF_18  HAVING PO_NO = '{fristPoNo}') T1, (SELECT PO_CMF_18 FROM MESMGR.MTAPCPOSTS GROUP BY PO_NO,PO_CMF_18  HAVING PO_NO = '{addPoNo}') T2";
                DataTable dtLabel = SqlUtils.SelectData(sql);
                if (dtLabel.Rows.Count == 0 || dtLabel.Rows[0][0].ToString().Equals("N"))
                {
                    return false;
                }
                return true;
            }
            return true;
        }
        #endregion

        #region  相同的芯片名称（执行单里面的芯片名称）

        private bool SameWaferName1(string addPoNo)
        {
            if (firstBox != null)
            {
                string fristPoNo = firstBox[3].ToString();
                if (fristPoNo.Equals(addPoNo))
                {
                    return true;
                }
                var table_ZLOTF = GetDBBy10_5_Excel(fristPoNo);
                var table_ZLOTA = GetDBBy10_5_Excel(addPoNo);
                string waferNameF = table_ZLOTF.Rows.Count != 0 ? table_ZLOTF.Rows[0]["Define20"].ToString() : "";
                string waferNameA = table_ZLOTA.Rows.Count != 0 ? table_ZLOTA.Rows[0]["Define20"].ToString() : "";
                if (!waferNameA.Equals(waferNameF))
                {
                    return false;
                }
                return true;
            }
            return true;
        }

        #endregion

        #region  相同的芯片名称（晶圆名称）

        private bool SameWaferName2(string addPoNo)
        {
            if (firstBox != null)
            {
                string fristPoNo = firstBox[3].ToString();
                if (fristPoNo.Equals(addPoNo))
                {
                    return true;
                }
                var table_ZLOTF = GetDBBy10_5_Excel(fristPoNo);
                var table_ZLOTA = GetDBBy10_5_Excel(addPoNo);
                string waferNameF = table_ZLOTF.Rows.Count != 0 ? table_ZLOTF.Rows[0]["Define20"].ToString() : "";
                string waferNameA = table_ZLOTA.Rows.Count != 0 ? table_ZLOTA.Rows[0]["Define20"].ToString() : "";
                if (!waferNameA.Equals(waferNameF))
                {
                    return false;
                }
                return true;
            }
            return true;
        }

        #endregion

        #region 相同的芯片版本（执行单里面的芯片版本）

        private bool SameWaferVerison(string addPoNo)
        {
            if (firstBox != null)
            {
                string fristPoNo = firstBox[3].ToString();
                if (fristPoNo.Equals(addPoNo))
                {
                    return true;
                }
                var table_ZLOTF = GetDBBy10_5_Excel(fristPoNo);
                var table_ZLOTA = GetDBBy10_5_Excel(addPoNo);
                string waferVersionF = table_ZLOTF.Rows.Count != 0 ? table_ZLOTF.Rows[0]["Define25"].ToString() : "";
                string waferVerisonA = table_ZLOTA.Rows.Count != 0 ? table_ZLOTA.Rows[0]["Define25"].ToString() : "";

                string pattern = @"版本号[：:](.*)";
                Match match1 = Regex.Match(waferVersionF, pattern);
                Match match2 = Regex.Match(waferVerisonA, pattern);
                string waferVer1 = "";
                string waferVer2 = "";
                if (match1.Success)
                {
                    waferVer1 = match1.Groups[1].Value;
                }
                else
                {
                    waferVer1 = "";
                }
                if (match2.Success)
                {
                    waferVer2 = match2.Groups[1].Value;
                }
                else
                {
                    waferVer2 = "";
                }

                if (!waferVer1.Equals(waferVer2))
                {
                    return false;
                }
                return true;

            }
            return true;
        }


        #endregion

        #region 相同的MARK第一行

        private bool SameMarkOneRow(string addPoNo)
        {
            if (firstBox != null)
            {
                string fristPoNo = firstBox[3].ToString();
                if (fristPoNo.Equals(addPoNo))
                {
                    return true;
                }
                string sql = $"SELECT SUB_LOT_ID_25  FROM MESMGR.MTAPCPOSTS WHERE PO_NO = '{addPoNo}' GROUP BY SUB_LOT_ID_25 ";
                DataTable dt1 = SqlUtils.SelectData(sql);
                string addMarkStr = dt1.Rows[0][0].ToString().Split('@')[0];
                sql = $"SELECT SUB_LOT_ID_25  FROM MESMGR.MTAPCPOSTS WHERE PO_NO = '{fristPoNo}' GROUP BY SUB_LOT_ID_25 ";
                DataTable dt2 = SqlUtils.SelectData(sql);
                string firstMarkStr = dt2.Rows[0][0].ToString().Split('@')[0];
                if (!addMarkStr.Equals(firstMarkStr))
                {
                    return false;
                }
                return true;

            }
            return true;
        }

        #endregion

        #region 最大的MARK内容不超过2个

        private bool MaxMarkValue(int maxValue, DataRow addBox)
        {
            string lot2 = addBox["LOT_ID2"].ToString();
            if (isFang.ContainsKey(lot2))
            {
                return isFang[lot2];
            }

            // 判断失败中是否有这个批次
            HashSet<string> temp = new HashSet<string>(marks);
            DataTable dt2 = lotPoInfo[lot2];
            foreach (DataRow dtLot in lotPoInfo[lot2].Rows)
            {
                string addPoNo = dtLot["PO_NO"].ToString();
                string sql = $"SELECT SUB_LOT_ID_25  FROM MESMGR.MTAPCPOSTS WHERE PO_NO = '{addPoNo}' GROUP BY SUB_LOT_ID_25 ";
                DataTable dt = SqlUtils.SelectData(sql);
                string markStr = dt.Rows[0][0].ToString();
                marks.Add(markStr);
                if (marks.Count > maxValue)
                {
                    marks = temp;
                    isFang.Add(lot2, false);
                    return false;
                }
            }
            isFang.Add(lot2, true);
            return true;

        }

        #endregion

        #region 最大的晶圆批号不超过2个

        private bool MaxWaferIdValue(int maxValue, DataRow addBox)
        {
            string lot2 = addBox["LOT_ID2"].ToString();
            if (isFang.ContainsKey(lot2))
            {
                return isFang[lot2];
            }
            HashSet<string> temp = new HashSet<string>(waferLots);
            foreach (DataRow dtLot in lotPoInfo[addBox["LOT_ID2"].ToString()].Rows)
            {
                string addPoNo = dtLot["PO_NO"].ToString();

                var table_ZLOT = GetDBBy10_5_Excel(addPoNo);
                string waferId = table_ZLOT.Rows.Count != 0 ? table_ZLOT.Rows[0]["Define19"].ToString() : "";
                waferLots.Add(waferId);
                if (waferLots.Count > maxValue)
                {
                    waferLots = temp;
                    isFang.Add(lot2, false);
                    return false;
                }
            }
            isFang.Add(lot2, true);
            return true;
        }

        #endregion

        #region 最大DC数量不超过2个（DC来自客户工单的年周号，执行单里面的年周号）

        private bool MaxDCValue1(int maxValue, DataRow addBox)
        {
            string lot2 = addBox["LOT_ID2"].ToString();
            if (isFang.ContainsKey(lot2))
            {
                return isFang[lot2];
            }
            HashSet<string> temp = new HashSet<string>(dateCodes);

            foreach (DataRow dtLot in lotPoInfo[addBox["LOT_ID2"].ToString()].Rows)
            {
                string addPoNo = dtLot["PO_NO"].ToString();
                var table_ZLOT = GetDBBy10_5_Excel(addPoNo);
                string dateCode = table_ZLOT.Rows.Count != 0 ? table_ZLOT.Rows[0]["Define46"].ToString() : "";
                if (dateCode != null && !dateCode.Equals(""))
                {
                    dateCodes.Add(dateCode);
                }
                if (dateCodes.Count > maxValue)
                {
                    //failDateCodeLots.Add(addBox["LOT_ID2"].ToString());
                    dateCodes = temp;
                    isFang.Add(lot2, false);
                    return false;
                }

            }
            isFang.Add(lot2, true);

            return true; ;
        }

        #endregion

        #region 最大DC数量不超过2个（Mark栏位信息）
        private bool MaxDCValue2(int maxValue,DataRow addBox)
        {
            string lot2 = addBox["LOT_ID2"].ToString();
            if (isFang.ContainsKey(lot2))
            {
                return isFang[lot2];
            }
            HashSet<string> temp = new HashSet<string>(cgwDateCodes);
            foreach (DataRow dtLot in lotPoInfo[addBox["LOT_ID2"].ToString()].Rows)
            {
                string lotId = dtLot["LOT_ID"].ToString();
                Dictionary<string, object> outNode = new Dictionary<string, object>();
                var CS = ReApi.HttpCallApi("http://10.16.10.5:9529/api/MarkingPID/Post_Printing_Lot?factory=TEST_NB",
    "{\"lotID\": \"" + lotId + "\" }");
                var obj_json = JObject.Parse(CS);
                var reslut = obj_json["result"];
                if (reslut.ToString() == "Y")
                {
                    var data = obj_json["data"]["Content"] as JArray;
                    if (data != null)
                    {
                        for (int idx_j = 0; idx_j < data.Count; idx_j++)
                        {
                            var Text = data[idx_j]["Text"].ToString();
                            Text = Text.Replace("&", string.Empty);
                            outNode.Add("PRINT_WORD_" + (idx_j + 1), Text);
                        }
                        bool flag = outNode.ContainsKey("PRINT_WORD_2");
                        string cgwDateCode = outNode.ContainsKey("PRINT_WORD_2") ? outNode["PRINT_WORD_2"].ToString() : "";
                        cgwDateCodes.Add(cgwDateCode);

                    }
                }

            }


            if (cgwDateCodes.Count > maxValue)
            {
                cgwDateCodes = temp;
                isFang.Add(lot2, false);
                return false;
            }
            isFang.Add(lot2, true);
            return true;
        }
        #endregion

        #region 最大LOT数量不超过2个（标签批号信息）

        private bool MaxLotQtyValue(int maxValue, DataRow addBox)
        {
            string lot2 = addBox["LOT_ID2"].ToString();
            if (isFang.ContainsKey(lot2))
            {
                return isFang[lot2];
            }
            HashSet<string> temp = new HashSet<string>(lblLots);

            foreach (DataRow dtLot in lotPoInfo[addBox["LOT_ID2"].ToString()].Rows)
            {
                string addPoNo = dtLot["PO_NO"].ToString();
                DataTable dt = SqlUtils.SelectData($"SELECT PO_NO ,PO_CMF_11  FROM MESMGR.MTAPCPOSTS GROUP BY PO_NO ,PO_CMF_11 HAVING  PO_NO = '{addPoNo}'");
                string lblLot = dt.Rows[0][0].ToString();
                lblLots.Add(lblLot);
                if (lblLots.Count > maxValue)
                {
                    lblLots = temp;
                    isFang.Add(lot2, false);
                    return false;
                }

            }

            isFang.Add(lot2, true);
            return true;
        }

        #endregion

        #region 相同的BD图纸

        private bool SameBDDaring(string addPoNo)
        {
            if (firstBox != null)
            {
                string fristPoNo = firstBox[3].ToString();
                if (fristPoNo.Equals(addPoNo))
                {
                    return true;
                }
                var table_ZLOTF = GetDBBy10_5_Excel(fristPoNo);
                var table_ZLOTA = GetDBBy10_5_Excel(addPoNo);
                string BDF = table_ZLOTF.Rows.Count != 0 ? table_ZLOTF.Rows[0]["Define41"].ToString() : "";
                string BDA = table_ZLOTA.Rows.Count != 0 ? table_ZLOTA.Rows[0]["Define41"].ToString() : "";
                if (!BDF.Equals(BDA))
                {
                    return false;
                }
                return true;
            }
            return true;
        }

        #endregion

        #region 相同的印字规范

        private bool SameMarkRule(string addPoNo)
        {
            if (firstBox != null)
            {
                string fristPoNo = firstBox[3].ToString();
                if (fristPoNo.Equals(addPoNo))
                {
                    return true;
                }
                string sql = $"SELECT SUB_LOT_ID_25  FROM MESMGR.MTAPCPOSTS WHERE PO_NO = '{addPoNo}' GROUP BY SUB_LOT_ID_25 ";
                DataTable dt1 = SqlUtils.SelectData(sql);
                string addMarkStr = dt1.Rows[0][0].ToString();
                sql = $"SELECT SUB_LOT_ID_25  FROM MESMGR.MTAPCPOSTS WHERE PO_NO = '{fristPoNo}' GROUP BY SUB_LOT_ID_25 ";
                DataTable dt2 = SqlUtils.SelectData(sql);
                string firstMarkStr = dt2.Rows[0][0].ToString();
                if (!addMarkStr.Equals(firstMarkStr))
                {
                    return false;
                }
                return true;

            }
            return true;
        }

        #endregion

        #region 相同的测试程序

        private bool SameTestProgram(string addPoNo)
        {
            if (firstBox != null)
            {
                string fristPoNo = firstBox[0].ToString();
                if (fristPoNo.Equals(addPoNo))
                {
                    return true;
                }
                //var table_ZLOTF = GetDBBy10_5_Excel(fristPoNo);
                //var table_ZLOTA = GetDBBy10_5_Excel(addPoNo);
                //string TESTF = table_ZLOTF.Rows.Count != 0 ? table_ZLOTF.Rows[0]["Define42"].ToString() : "";
                //string TESTA = table_ZLOTA.Rows.Count != 0 ? table_ZLOTA.Rows[0]["Define42"].ToString() : "";
                Task<string> TESTF = GetReceiptData(fristPoNo);
                Task<string> TESTA = GetReceiptData(addPoNo);
                if (!TESTF.Result.Equals(TESTA.Result))
                {
                    return false;
                }
                return true;
            }
            return true;
        }

        #endregion

        #region 相同的LOT TYPE

        private bool SameLotType(string addLotId)
        {
            if (firstBox != null)
            {
                string fristLotId = firstBox[0].ToString();
                if (fristLotId.Equals(addLotId))
                {
                    return true;
                }
                string sql = $"SELECT CASE WHEN T1.LOT_TYPE = T2.LOT_TYPE  THEN 'Y'  ELSE 'N'  END AS IS_EQUAL FROM ( SELECT  LOT_TYPE FROM  MESMGR.MWIPLOTSTS WHERE  LOT_ID = '{fristLotId}') T1 ,( SELECT  LOT_TYPE FROM  MESMGR.MWIPLOTSTS WHERE  LOT_ID = '{addLotId}') T2";
                DataTable dt = SqlUtils.SelectData(sql);
                if (dt.Rows.Count == 0 || dt.Rows[0][0].ToString().Equals("N"))
                {
                    return false;
                }
                return true;
            }
            return true;
        }

        #endregion

        #endregion

        #region 获取执行单数据
        public DataTable GetDBBy10_5_Excel(string mo)
        {
            try
            {
                string Sono_Sql = string.Format("select * from [dbo].[crm_ordertosap] t where Define9 = '{0}'", mo);
                var connStr = "server=10.16.10.5;database=WCMA;uid=sa;pwd=trsnb!234;MultipleActiveResultSets=true;Connect Timeout=18000";
                SqlConnection conn = null;
                conn = new SqlConnection(connStr);
                conn.Open();
                SqlDataAdapter sda = new SqlDataAdapter(Sono_Sql, conn);
                DataSet ds = new DataSet();
                sda.Fill(ds);
                var GD_EXCEL_DATA = ds.Tables[0];
                conn.Close();
                return GD_EXCEL_DATA;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
        #endregion

        #region 获取TMS中的数据
        public async Task<string> GetReceiptData(string lotId)
        {
            try
            {
                string sql = $"SELECT MAT_ID ,RESV_FIELD_3  FROM MESMGR.MWIPLOTSTS WHERE LOT_ID='{lotId}'";
                DataTable dt = SqlUtils.SelectData(sql);
                string matId = dt.Rows[0][0].ToString();
                string eqpId = dt.Rows[0][1].ToString();
                string url = "http://10.16.10.5:9528/swagger";

                var data = new DataModel
                {
                    pid = matId,
                    eqp_ID = eqpId
                };
                string jsonData = JsonConvert.SerializeObject(data);
                var client = new HttpClient();
                client.BaseAddress = new Uri(url);
                var postData = new StringContent(jsonData, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync("api/TMS/RecipeSynchronization", postData);
                string responseContent = await response.Content.ReadAsStringAsync();
                JObject jasonData = JObject.Parse(responseContent);
                string Recipe = jasonData["FT_Data"][0]["Recipe"].ToString();

                return Recipe;
                
            }
            catch (Exception ex)
            {
                return "";
            }
            
        }
        #endregion

        #region 获取Bin别
        public static string GetBinGradeByLotId(string LotId, string customerId, string matId, bool binType)
        {

            string queryBinGradeSql = "";
            if (binType)
            {
                queryBinGradeSql = $"SELECT BIN_PROMPT  FROM MESMGR.MWIPBINSHS WHERE CHILD_LOT_ID='{LotId}' AND BIN_PROMPT!='LEFT' AND FACTORY='TEST_NB' AND HIST_DEL_FLAG!='Y' AND CHILD_MAT_ID='{matId}' AND BIN_TYPE = 'P' AND LOT_ID LIKE '{LotId.Substring(0, 14)}%' ";
            }
            else
            {
                queryBinGradeSql = $"SELECT BIN_PROMPT  FROM MESMGR.MWIPBINSHS WHERE CHILD_LOT_ID='{LotId}' AND BIN_PROMPT!='LEFT' AND FACTORY='TEST_NB' AND HIST_DEL_FLAG!='Y' AND CHILD_MAT_ID='{matId}' AND LOT_ID LIKE '{LotId.Substring(0, 14)}%' ";
            }

            #region 获取批次BIN采集的BIN别
            string binGrade = SqlUtils.SelectData(queryBinGradeSql).Rows.Count == 0 ? "" : SqlUtils.SelectData(queryBinGradeSql).Rows[0][0].ToString();
            if (binGrade == "")
            {
                binGrade = "HBIN1";
            }
           /* string queryBinGradeByCustomer = $"SELECT DATA_1 FROM MGCMTBLDAT WHERE TABLE_NAME='BIN_LABEL_RELATION' AND FACTORY='TEST_NB' AND KEY_1='{customerId}' AND KEY_2='{binGrade}'";
            string customerBinGrade = SqlUtils.SelectData(queryBinGradeByCustomer).Rows.Count == 0 ? "" : SqlUtils.SelectData(queryBinGradeByCustomer).Rows[0][0].ToString();*/
            return binGrade;

            #endregion
        }
        #endregion

        #endregion

        #region 获取客户号

        public string GetCustId(ReceiveInsp shipInsp)
        {
            // 确认客户号
            if (shipInsp.consumerId == null || shipInsp.consumerId.Equals(""))
            {
                string sql = "";

                switch (shipInsp.type)
                {
                    case 2:
                        sql = $"SELECT CUSTOMER_ID  FROM MESMGR.MTAPCPOSTS WHERE PO_CMF_12 = '{shipInsp.poNo}'";
                        break;
                    case 3:
                        sql = $"SELECT CUSTOMER_ID  FROM MESMGR.MTAPCPOSTS WHERE PO_NO = '{shipInsp.moNo}'";
                        break;
                    case 4:
                        sql = $"SELECT LOT_CMF_2  FROM MESMGR.MWIPLOTSTS m WHERE LOT_ID = '{shipInsp.subLotId}'";
                        break;
                    case 5:
                        sql = $"SELECT T2.LOT_CMF_2  FROM MESMGR.CTAPOBXSTS T1 LEFT JOIN MESMGR.MWIPLOTSTS  T2 ON T1.LOT_ID = T2.LOT_ID WHERE T1.BOX_ID = '{shipInsp.boxId}'";
                        break;
                }
                DataTable dtCustid = SqlUtils.SelectData(sql);
                if(dtCustid.Rows.Count == 0)
                {
                    return null;
                }
                return dtCustid.Rows[0][0].ToString();
            }
            return null;
        }

        #endregion

        #region 获取合批
        public void GetFromIdByLot(string lot,DataTable dtMergeLots)
        {

            string queryFromLotId = "SELECT H.LOT_ID AS LOT_ID, H.ORDER_ID AS PO_NO " +
                "FROM MESMGR.MWIPLOTHIS H " +
                "JOIN MESMGR.MWIPLOTSTS T1 " +
                "ON H.FROM_TO_LOT_ID = T1.LOT_ID " +
                "WHERE H.FACTORY IN ('ASSY_NB', 'TEST_NB') " +
                "AND H.HIST_DEL_FLAG != 'Y' " +
                "AND H.TRAN_CODE = 'MERGE' " +
                "AND H.LOT_ID NOT LIKE '%DB%' " +
                "AND H.FROM_TO_FLAG = 'F' " +
                $"AND SUBSTR(H.LOT_ID,0,14) != SUBSTR('{lot}',0,14) " +
                $"AND T1.LOT_ID='{lot}'";
                           
            DataTable fromLotList = SqlUtils.SelectData(queryFromLotId);

            if (fromLotList != null || fromLotList.Rows.Count != 0)
            {
                foreach (DataRow dr in fromLotList.Rows)
                {
                    dtMergeLots.Rows.Add(dr.ItemArray);
                    this.GetFromIdByLot(dr["LOT_ID"].ToString(),dtMergeLots);
                }
            }
            return ;
        }
        #endregion

        #region Clear
        private void DataClear()
        {
            firstBox = null;
            dateCodes.Clear();
            waferLots.Clear();
            marks.Clear();
            lblLots.Clear();
            cgwDateCodes.Clear();
            isFang.Clear();
        }

        private void AllClear()
        {
            firstBox = null;
            dateCodes.Clear();
            waferLots.Clear();
            marks.Clear();
            lblLots.Clear();
            cgwDateCodes.Clear();
            mergeBoxRules.Clear();
            currentQty = 0;
            boxQty = 0;
            deptBoxs.Clear();
            flagRuleType = false;
            maxQty = 0;
        }

        #endregion
    }
}
