namespace ASP_Entity_Freamwork_Study.Entity
{
    public class ReceiveInsp
    {
        /*
         * 出货单号
         */
        public string shipNo { get; set; }
        /*
         * 指令类型
         */
        public int type {  get; set; }

        /*
         * 产品型号
         */
        public string prodModel { get; set; }
        /*
         * 数量
         */
        public int qty { get; set; }
        /*
         * 客户号
         */
        public string consumerId { get; set; }
        /*
         * MO 号
         */
        public string moNo {  get; set; }
        /*
         * PO 号
         */
        public string poNo { get; set; }
        /*
         * 内盒号
         */
        public string boxId { get; set; }
        /*
         * 子批次号
         */
        public string subLotId { get; set; }
        /*
         * 接收时间
         */
        public string shipDate { get; set; }
        /*
         * 预计出货时间
         */
        public string expectShipTime { get; set; }  
    }
}
