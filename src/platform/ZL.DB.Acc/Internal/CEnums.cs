namespace ZL.DB.Acc.Utils
{
    public class AccessClientInfo
    {
        public string ActionFun { get; set; }
        public string ActionSub { get; set; }
        public string ActionType { get; set; }
        public string IFWinID { get; set; }
        public string LGGID { get; set; }
        public string TableLogOff { get; set; }
        public string TraceInfo { get; set; }
        public string UserID { get; set; }
    }
    public enum DbKinds { Odbc, OleDb, Oracle, Sql, MySql, Sqlite, Null }
}
