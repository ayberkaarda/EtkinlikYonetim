using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;

public class DatabaseHelper
{
    // Connection string: ETKINLIK_CONNECTION environment variable, otherwise a local SQL Server Express instance.
    private static string connectionString =
        Environment.GetEnvironmentVariable("ETKINLIK_CONNECTION")
        ?? "Server=localhost\\SQLEXPRESS;Database=EtkinlikYonetim;Trusted_Connection=True;TrustServerCertificate=True;";

    /// <summary>
    /// INSERT, UPDATE, DELETE gibi sorguları çalıştırır.
    /// </summary>
    public static void ExecuteNonQuery(string query, params SqlParameter[] parameters)
    {
        using (var conn = new SqlConnection(connectionString))
        {
            conn.Open();
            using (var cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddRange(parameters);
                cmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>
    /// SELECT sorgularını çalıştırır ve sonucu DataTable olarak döner.
    /// </summary>
    public static DataTable ExecuteQuery(string query, params SqlParameter[] parameters)
    {
        using (var conn = new SqlConnection(connectionString))
        {
            conn.Open();
            using (var cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddRange(parameters);
                using (var adapter = new SqlDataAdapter(cmd))
                {
                    DataTable dt = new DataTable();
                    adapter.Fill(dt);
                    return dt;
                }
            }
        }
    }
}
