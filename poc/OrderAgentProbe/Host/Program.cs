using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class Probe
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 3 || !System.Text.RegularExpressions.Regex.IsMatch(args[1], @"\AChaosOrderAgent_[0-9a-f]{32}\z") ||
            (args[2] != "baseline" && args[2] != "inject"))
            throw new ArgumentException("Expected sample EXE, unique test DB and baseline or inject mode.");
        var db = args[1];
        var inject = args[2] == "inject";
        const string master = @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true";
        var connection = master.Replace("Database=master", "Database=" + db);
        Exec(master, "CREATE DATABASE [" + db + "]");
        try
        {
            foreach (var command in new[]
            {
                "CREATE SCHEMA [Application]", "CREATE SCHEMA Sales", "CREATE SCHEMA Warehouse", "CREATE SCHEMA Website",
                "CREATE TABLE [Application].People(PersonID int, IsEmployee bit); INSERT [Application].People VALUES (1,1)",
                "CREATE TABLE Sales.Customers(CustomerID int, PrimaryContactPersonID int, DeliveryAddressLine1 nvarchar(40), DeliveryAddressLine2 nvarchar(40)); INSERT Sales.Customers VALUES (1,1,N'A',N'B')",
                "CREATE TABLE Warehouse.StockItems(StockItemID int, StockItemName nvarchar(40), IsChillerStock bit); INSERT Warehouse.StockItems VALUES (1,N'Item',0)",
                "CREATE TABLE dbo.Received(Id int IDENTITY)",
                "CREATE TYPE Website.OrderList AS TABLE(OrderReference int, CustomerID int, ContactPersonID int, ExpectedDeliveryDate date, CustomerPurchaseOrderNumber nvarchar(20), IsUndersupplyBackordered bit, Comments nvarchar(40), DeliveryInstructions nvarchar(100))",
                "CREATE TYPE Website.OrderLineList AS TABLE(OrderReference int, StockItemID int, Description nvarchar(40), Quantity int)"
            }) Exec(connection, command);

            const string signature = " PROCEDURE Website.InsertCustomerOrders @Orders Website.OrderList READONLY, @OrderLines Website.OrderLineList READONLY, @OrdersCreatedByPersonID int, @SalespersonPersonID int AS ";
            Exec(connection, "CREATE" + signature + "INSERT dbo.Received DEFAULT VALUES");
            GC.KeepAlive(typeof(ChaosOrderBridge.Dispatch).Assembly);
            var type = Assembly.LoadFrom(args[0]).GetType("MultithreadedInMemoryTableInsert.MultithreadedOrderInsertMain", true);
            using (var form = (Form)Activator.CreateInstance(type))
            {
                form.ShowInTaskbar = false;
                form.Show();
                form.FormClosing += (sender, e) => e.Cancel = true;
                ((TextBox)Field(form, "ConnectionStringTextBox")).Text = connection;
                ((NumericUpDown)Field(form, "NumberOfThreadsNumericUpDown")).Value = 1;
                var button = (Button)Field(form, "InsertButton");
                button.PerformClick();
                var worker = ((Thread[])Field(form, "sqlTasks")).Single();
                if (!inject)
                {
                    Wait(() => (long)Field(form, "totalOrders") >= 1 || !worker.IsAlive);
                    if (!worker.IsAlive) throw new Exception("Baseline worker stopped: " + Field(form, "errorDetails"));
                    button.PerformClick();
                    Wait(() => !worker.IsAlive);
                    var rows = Count(connection);
                    Console.WriteLine("Baseline orders inserted: " + rows);
                    if (rows < 1) throw new Exception("Baseline produced no rows.");
                    return;
                }

                Wait(() => !worker.IsAlive);
                var observed = Stopwatch.StartNew();
                bool dialog = false;
                while (observed.ElapsedMilliseconds < 2500)
                {
                    Application.DoEvents();
                    dialog |= Application.OpenForms.Cast<Form>().Any(window => window != form && window.Visible);
                    Thread.Sleep(15);
                }

                var details = (string)Field(form, "errorDetails");
                var after = Count(connection);
                Console.WriteLine("Worker alive: " + worker.IsAlive);
                Console.WriteLine("Error flag: " + Field(form, "errorHasOccurred"));
                Console.WriteLine("Injected TimeoutException recorded: " + details.Contains("System.TimeoutException: Windows Chaos Agent: timeout before SQL send."));
                Console.WriteLine("Visible error dialog: " + dialog);
                Console.WriteLine("Insert button label: " + button.Text);
                Console.WriteLine("Rows during fault: " + after);
                if (worker.IsAlive || !details.Contains("System.TimeoutException: Windows Chaos Agent: timeout before SQL send.") || after != 0)
                    throw new Exception("Unexpected probe result.");
            }
        }
        finally
        {
            SqlConnection.ClearAllPools();
            Exec(master, "ALTER DATABASE [" + db + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + db + "]");
        }
    }

    private static object Field(Form form, string name)
    {
        return form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(form);
    }

    private static void Wait(Func<bool> predicate)
    {
        var time = Stopwatch.StartNew();
        while (!predicate())
        {
            if (time.ElapsedMilliseconds > 10000) throw new TimeoutException("Probe wait expired.");
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void Exec(string connection, string sql)
    {
        using (var con = new SqlConnection(connection))
        using (var command = new SqlCommand(sql, con))
        {
            con.Open();
            command.ExecuteNonQuery();
        }
    }

    private static int Count(string connection)
    {
        using (var con = new SqlConnection(connection))
        using (var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Received", con))
        {
            con.Open();
            return (int)command.ExecuteScalar();
        }
    }
}
