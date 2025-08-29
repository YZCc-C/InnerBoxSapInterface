using ASP_Entity_Freamwork_Study.Entity;
using ASP_Entity_Freamwork_Study.Utils;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.CompilerServices;
using System.Transactions;

namespace Miracom.WEBCore.Service.impl
{
    public class MesCancelStockServiceImpl : MesCancelStockService
    {
        public Result CancelStock(string pickListNo,string userId)
        {
            //List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            var Result = new Result 
            {
                code=0,
                message ="",
                //data = new List<Dictionary<string, object>>()
            };
            // 配置事务选项
            var transactionOptions = new TransactionOptions
            {
                IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted,
                Timeout = TimeSpan.FromSeconds(300) // 设置适当超时
            };

            using (var scope = new TransactionScope(TransactionScopeOption.Required, transactionOptions))
            {
                try
                {
                    //通过拣货单号查OUT_BOX_ID(PICKLIST表)
                    string queryBoxInfo = $"SELECT * FROM PICKLIST WHERE PICK_LIST_NO = '{pickListNo}'";
                    DataTable OutBoxLists = SqlUtils.SelectData(queryBoxInfo);
                    var outBoxIdList = OutBoxLists.AsEnumerable()
                        .Select(row => row["OUT_BOX_ID"].ToString())
                        .Distinct()
                        .ToList();
                    var innerBoxIdList = OutBoxLists.AsEnumerable()
                        .Select(row => row["IN_BOX_ID"].ToString())
                        .Distinct()
                        .ToList();


                    if (outBoxIdList.Count == 0|| innerBoxIdList.Count==0)
                    {
                        Result.data = "没有要操作的数据";
                        Result.code = 220;
                        Result.message = "没有要操作的数据";
                        return Result;
                    }

                    //1. 通过拣货单号删除PICKLIST表数据
                    string deletePickListSql = $"DELETE FROM PICKLIST WHERE PICK_LIST_NO = '{pickListNo}'";
                    int pickListRows = SqlUtils.ExcuteSingleCRD(deletePickListSql);

                    //2.根据outboxId删除数据 
                    string boxIdsCondition = string.Join(",", outBoxIdList);

                    // 删除表中的数据
                    string deleteCobxsSql = $"DELETE COBXIBXSTS  WHERE OUTBOX_ID IN ('{boxIdsCondition}') AND OUT_BOX_TYPE='OUT'";//这个表内盒也要删吧--不用
                    int cobxiRows = SqlUtils.ExcuteSingleCRD(deleteCobxsSql);
                    string deleteCobxhSql = $"DELETE COBXIBXHIS  WHERE OUTBOX_ID IN ('{boxIdsCondition}') AND OUT_BOX_TYPE='OUT'";//这个表内盒也要删吧--不用
                    int cobxhRows = SqlUtils.ExcuteSingleCRD(deleteCobxhSql);
                    string deleteCtaposSql = $"DELETE CTAPOBXSTS  WHERE BOX_ID IN ('{boxIdsCondition}') AND BOX_TYPE='OUT'";
                    int ctaposRows = SqlUtils.ExcuteSingleCRD(deleteCtaposSql);
                    string deleteCtapohSql = $"DELETE CTAPOBXHIS  WHERE BOX_ID IN ('{boxIdsCondition}') AND BOX_TYPE='OUT'";
                    int ctapohRows = SqlUtils.ExcuteSingleCRD(deleteCtapohSql);


                    //3.MTAPLBLHIS                    
                    //string updateMtapSql = $"UPDATE MTAPLBLHIS SET PRN_HIS_CMF_16 = ' ', UPDATE_TIME = '{DateTime.Now}', UPDATE_USER = '{userId}' WHERE PRN_CMF_15 = '{shipId}'";
                    string updateMtapSql = $"UPDATE MTAPLBLHIS SET PRN_HIS_CMF_16 = ' ', UPDATE_TIME = '{DateTime.Now.ToString("yyyyMMddHHmmss")}', UPDATE_USER_ID = '{userId}' WHERE PRN_HIS_CMF_16 IN ('{boxIdsCondition}')";
                    int mtapRows = SqlUtils.ExcuteSingleCRD(updateMtapSql);

                    //4. SHIPINSP--innerBOX_ID; INNERBOXSHELFINFO--innerBoxId(B10EG54400);  WAREHOUSEERECEIPT--innerBoxId
                    int shipinspRows = 0;
                    int innerBoxShelfRows = 0;
                    int warehouserecRows = 0;
                    foreach (var inboxId in innerBoxIdList){
                        shipinspRows = shipinspRows + SqlUtils.ExcuteSingleCRD($"DELETE FROM SHIPINSP WHERE BOX_ID = '{inboxId}'");
                        innerBoxShelfRows = innerBoxShelfRows + SqlUtils.ExcuteSingleCRD($"DELETE FROM INNERBOXSHELFINFO WHERE INNER_BOX_ID = '{inboxId}'");
                        warehouserecRows = warehouserecRows + SqlUtils.ExcuteSingleCRD($"UPDATE WAREHOUSERECEIPT SET STATE ='4' WHERE INNER_BOX_ID = '{inboxId}'");
                    }

                    //5. IERPFGIRPT   CTAPFGSDAT
                    string deleteFerpSql = $"DELETE FROM IERPFGIRPT WHERE FGIRPT_CMF_45 IN ('{boxIdsCondition}')";
                    int ferpRows = SqlUtils.ExcuteSingleCRD(deleteFerpSql);
                    string deleteCtaSql = $"DELETE FROM CTAPFGSDAT WHERE BOX_ID IN ('{boxIdsCondition}')";
                    int ctaRows = SqlUtils.ExcuteSingleCRD(deleteCtaSql);

                    int totalCount = pickListRows + cobxiRows + cobxhRows + ctaposRows + ctapohRows + mtapRows + shipinspRows + innerBoxShelfRows + warehouserecRows + ferpRows + ctaRows;
                    #region
                    //// 第一步：先查询所有需要的数据（在删除前）
                    //// 1. 获取OUTBOX_ID列表
                    //string queryBoxInfo = $"SELECT * FROM MTAPLBLHIS WHERE PRN_HIS_CMF_15 = '{shipId}'";
                    //DataTable BoxLists = SqlUtils.SelectData(queryBoxInfo);
                    //var outBoxIdList = BoxLists.AsEnumerable()
                    //    .Select(row => row["PRN_HIS_CMF_16"].ToString())
                    //    .Distinct()
                    //    .ToList();

                    //// 2. 查询COBXIBXSTS获取所有INBOX_ID（在删除前先查询）
                    //Dictionary<string, List<string>> inboxIdMap = new Dictionary<string, List<string>>();
                    //foreach (var outboxId in outBoxIdList)
                    //{
                    //    string query = "SELECT * FROM COBXIBXSTS WHERE OUTBOX_ID = :outboxId";
                    //    var parameters = new List<OracleParameter> {
                    //        new OracleParameter(":outboxId", outboxId)
                    //    };
                    //    DataTable dt = SqlUtils.SelectData(query, parameters);
                    //    inboxIdMap[outboxId] = dt.AsEnumerable()
                    //        .Select(row => row["INBOX_ID"].ToString())
                    //        .ToList();
                    //}



                    //// 1. 删除IERPFGIRPT表中的数据
                    //string deleteFerpSql = $"DELETE FROM IERPFGIRPT WHERE FGIRPT_CMF_53 = '{shipId}'";
                    //int ferpRows = SqlUtils.ExcuteSingleCRD(deleteFerpSql);

                    //// 2. 删除CTAPFGSDAT表中的数据
                    //string deleteCtaSql = $"DELETE FROM CTAPFGSDAT WHERE FGS_DAT_CMF_1 = '{shipId}'";
                    //int ctaRows = SqlUtils.ExcuteSingleCRD(deleteCtaSql);

                    //// 3. 
                    //string updateMtapSql = $"UPDATE MTAPLBLHIS SET PRN_HIS_CMF_16 = ' ', UPDATE_TIME = '{DateTime.Now}', UPDATE_USER = '{userId}' WHERE PRN_CMF_15 = '{shipId}'";
                    //int mtapRows = SqlUtils.ExcuteSingleCRD(updateMtapSql);

                    //// 4. COBXIBXSTS  COBXIBXHIS  CTAPOBXSTS  CTAPOBXHIS  通过 OUTBOX_ID  BOX_TYPE删除 数据
                    ////string queryBoxInfo = $"SELECT * FROM MTAPLBLHIS WHERE PRN_CMF_15 = '{shipId}'"; 
                    ////DataTable BoxLists = SqlUtils.SelectData(queryBoxInfo);

                    //// 构建删除条件
                    //var OutBoxIds = BoxLists.AsEnumerable()
                    //    .Select(row => $"'{row["PRN_HIS_CMF_16"]}'")
                    //    .Distinct()
                    //    .ToList();

                    //string boxIdsCondition = string.Join(",", OutBoxIds);

                    //// 删除表中的数据
                    //string deleteCobxsSql = $"DELETE COBXIBXSTS  WHERE OUTBOX_ID IN ({boxIdsCondition}) AND OUT_BOX_TYPE='OUT'";
                    //int cobxiRows = SqlUtils.ExcuteSingleCRD(deleteCobxsSql);//1
                    //string deleteCobxhSql = $"DELETE COBXIBXHIS  WHERE OUTBOX_ID IN ({boxIdsCondition}) AND OUT_BOX_TYPE='OUT'";
                    //int cobxhRows = SqlUtils.ExcuteSingleCRD(deleteCobxhSql);//2
                    //string deleteCtaposSql = $"DELETE CTAPOBXSTS  WHERE OUTBOX_ID IN ({boxIdsCondition}) AND OUT_BOX_TYPE='OUT'";
                    //int ctaposRows = SqlUtils.ExcuteSingleCRD(deleteCtaposSql);//3
                    //string deleteCtapohSql = $"DELETE CTAPOBXHIS  WHERE OUTBOX_ID IN ({boxIdsCondition}) AND OUT_BOX_TYPE='OUT'";
                    //int ctapohRows = SqlUtils.ExcuteSingleCRD(deleteCtapohSql);//4

                    ////6. SHIPINSP ---内盒BOX_ID;  INNERBOXSHELFINFO;  WAREHOUSEERECEIPT
                    //int shipinspRows = 0;
                    //int innerBoxShelfRows = 0;
                    //int warehouserecRows = 0;
                    //for (int i = 0; i < OutBoxIds.Count; i++)
                    //{
                    //    // 遍历所有INBOX_ID
                    //    List<string> innerBoxIdList = new List<string>();
                    //    foreach (var inboxIds in inboxIdMap.Values)
                    //    {
                    //        foreach (var inboxId in inboxIds)
                    //        {
                    //            innerBoxIdList.Add(inboxId);
                    //            shipinspRows = shipinspRows + SqlUtils.ExcuteSingleCRD($"DELETE FROM SHIPINSP WHERE BOX_ID = '{inboxId}'");
                    //            innerBoxShelfRows = innerBoxShelfRows+ SqlUtils.ExcuteSingleCRD($"DELETE FROM INNERBOXSHELFINFO WHERE BOX_ID = '{inboxId}'");
                    //            warehouserecRows = warehouserecRows + SqlUtils.ExcuteSingleCRD($"UPDATE WAREHOUSERECEIPT SET STATE ='4' WHERE BOX_ID = '{inboxId}'");
                    //        }
                    //    }

                    //    //7. PickList
                    //    foreach (var outBoxId in outBoxIdList)
                    //    {
                    //        string deletePickListQuery = $"DELETE FROM PICKLIST WHERE OUT_BOX_ID = '{outBoxId}'";
                    //        int pickListRows = SqlUtils.ExcuteSingleCRD(deletePickListQuery);
                    //    }

                    //}

                    // 记录操作结果
                    //result.Add(new Dictionary<string, object>
                    //{
                    //    { "IERPFGIRPT_DeletedRows", ferpRows },
                    //    { "CTAPFGSDAT_DeletedRows", ctaRows },
                    //    { "MTAPLBLHIS_UpdatedRows", mtapRows },
                    //    { "COBXIBXSTS_DeletedRows", cobxiRows },
                    //    { "COBXIBXHIS_DeletedRows", cobxhRows },
                    //    { "CTAPOBXSTS_DeletedRows", ctaposRows },
                    //    { "CTAPOBXHIS_DeletedRows", ctapohRows },

                    //    { "SHIPINSP_DeletedRows", shipinspRows },
                    //    { "INNERBOXSHELFINFO_DeletedRows", innerBoxShelfRows },
                    //    { "WAREHOUSEERECEIPT_UpdatedRows", warehouserecRows }
                    //});
                    #endregion
                    scope.Complete(); // 提交事务

                    Result.data = "执行成功,操作数据"+ totalCount + "条。"+"PICKLIST Delete："+ pickListRows 
                                + ";COBXIBXSTS Delete："+ cobxiRows + ";COBXIBXHIS Delete:" + cobxhRows + ";CTAPOBXSTS Delete:" + ctaposRows + ";CTAPOBXHIS Delete:"+ ctapohRows
                                + ";MTAPLBLHIS Update:" + mtapRows + ";SHIPINSP Delete:" + shipinspRows + ";INNERBOXSHELFINFO Delete:" + innerBoxShelfRows + ";WAREHOUSERECEIPT Update:" + warehouserecRows
                                + ";IERPFGIRPT Delete:" + ferpRows + ";CTAPFGSDAT Delete:" + ctaRows;
                    Result.code = 200;
                    Result.message = "OK";
                }
                catch (Exception ex)
                {
                    // 记录完整错误信息
                    Console.WriteLine($"事务执行失败: {ex.ToString()}");

                    // 可以根据需要添加特殊处理
                    if (ex is TransactionAbortedException)
                    {
                        Console.WriteLine("事务已中止");
                    }
                    else if (ex is TransactionInDoubtException)
                    {
                        Console.WriteLine("事务状态不确定");
                    }

                    Result.code = 500;
                    Result.data = "执行失败";
                    Result.message = ex.ToString();
                    throw; // 重新抛出异常，确保事务回滚
                }
            }

            return Result;
        }
    }
}
