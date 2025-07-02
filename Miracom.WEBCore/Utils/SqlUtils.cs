using Oracle.ManagedDataAccess.Client;
using System.Data;

namespace ASP_Entity_Freamwork_Study.Utils
{
    public class SqlUtils
    {
        //   private static string connectionString = "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=192.168.99.143)(PORT=1522))(CONNECT_DATA=(SERVICE_NAME=MESDB)));User Id=XX001;Password=G01-XX01;";

        private static string connectionString = ConfigHelper.GetMesConnectionString();
        public static DataTable SelectData(string sql)
        {
            
            using (OracleConnection connection = new OracleConnection(connectionString))
            {
                DataTable dt = new DataTable();
                try
                {
                    connection.Open();
                    using (OracleCommand command = new OracleCommand(sql, connection))
                    {
                        using (OracleDataReader reader = command.ExecuteReader())
                        {
                            for (global::System.Int32 i = 0; i < reader.FieldCount; i++)
                            { 
                                dt.Columns.Add(reader.GetName(i));
                            }
                                while (reader.Read())
                            {
                                DataRow dr = dt.NewRow();
                                for (global::System.Int32 i = 0; i < reader.FieldCount; i++)
                                {
                                    dr[i] = reader.GetValue(i);
                                }
                                dt.Rows.Add(dr);
                            }
                        }
                    }
                    // 连接成功后的操作
                    Console.WriteLine("Connected to Oracle Database successfully!");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: 数据库连接失败" + ex.Message + "  --数据库错误");
                }
                finally
                {
                    connection.Close();
                }
                return dt;
            }
        }

        public static int ExcuteSingleCRD(string sql) 
        {
            int rowChange = 0;
            using (OracleConnection connection = new OracleConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    using (OracleCommand command = new OracleCommand(sql,connection))
                    {
                        rowChange = command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex) 
                {
                    Console.WriteLine("Error: " + ex.Message + "  --数据库错误");
                }
                finally
                {
                    connection.Close();
                }
            }
            return rowChange;
        }

    }
}
