using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Data.SqlClient;

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

        public static DataTable SelectData(string sql, List<OracleParameter> parameters)
        {
            using (OracleConnection connection = new OracleConnection(connectionString))
            {
                DataTable dt = new DataTable();
                try
                {
                    connection.Open();
                    using (OracleCommand command = new OracleCommand(sql, connection))
                    {
                        if (parameters != null && parameters.Count > 0)
                        {
                            command.Parameters.AddRange(parameters.ToArray());
                        }

                        using (OracleDataReader reader = command.ExecuteReader())
                        {
                            // 动态构建DataTable结构
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                dt.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
                            }

                            // 填充数据
                            while (reader.Read())
                            {
                                DataRow row = dt.NewRow();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                                }
                                dt.Rows.Add(row);
                            }
                        }
                    }
                    Console.WriteLine("Oracle查询执行成功");
                }
                catch (OracleException ex)
                {
                    Console.WriteLine($"Oracle错误[{ex.Number}]: {ex.Message}");
                    dt = new DataTable(); // 返回空表避免null引用
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"系统错误: {ex.Message}");
                    dt = new DataTable();
                }
                return dt;
            }
        }

        //public static DataTable SelectData(string sql, List<OracleParameter> parameters)
        //{
        //    DataTable dataTable = new DataTable();

        //    using (var connection = new OracleConnection(connectionString))
        //    {
        //        connection.Open();

        //        using (var command = new OracleCommand(sql, connection))
        //        {
        //            if (parameters != null)
        //            {
        //                command.Parameters.AddRange(parameters.ToArray());
        //            }

        //            using (var adapter = new OracleDataAdapter(command))
        //            {
        //                adapter.Fill(dataTable);
        //            }

        //            //using (OracleDataReader reader = command.ExecuteReader())
        //            //{
        //            //    dataTable.Load(reader); // 使用Load方法简化填充
        //            //}
        //        }
        //    }

        //    return dataTable;
        //}

        public static int ExcuteSingleCRD(string sql)
        {
            int rowChange = 0;
            using (OracleConnection connection = new OracleConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    Console.WriteLine("Executing SQL: " + sql); // 打印SQL
                    using (OracleCommand command = new OracleCommand(sql, connection))
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


        /// <summary> /// 执行SQL语句并返回受影响的行数 /// </summary> 
        /// /// <param name="sql">要执行的SQL语句</param> 
        /// /// <returns>受影响的行数，执行出错返回-1</returns> 
        public static int ExecuteNonQuery(string sql)
        {     // 参数校验
            if (string.IsNullOrWhiteSpace(sql))
            { 
                throw new ArgumentNullException(nameof(sql), "SQL语句不能为空"); 
            }
            try
            {
                using (var connection = new OracleConnection(connectionString))
                {
                    connection.Open(); 
                    Console.WriteLine("Executing SQL: " + sql); // 打印SQL
                    // 添加SQL日志记录
                    using (var command = new OracleCommand(sql, connection))
                    {
                        // 设置合理的命令超时时间(单位：秒)
                        command.CommandTimeout = 30;
                        int affectedRows = command.ExecuteNonQuery();
                        return affectedRows;
                    }
                }
            }
            catch (OracleException ex)
            // 捕获特定数据库异常
            {
                return -1;
            }
            catch (Exception ex) // 捕获其他异常
            {
                return -1;
            }
        }
    }
}
