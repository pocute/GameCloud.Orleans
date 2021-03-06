﻿// Copyright (c) Cragon. All rights reserved.

namespace GameCloud.Unity.Common
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.IO;

    public enum EbFieldType
    {
        None = 0,
        Int,
        Float,
        String
    }

    public class EbDataMgr
    {
        //---------------------------------------------------------------------
        Dictionary<string, EbTableBuffer> mMapTable = new Dictionary<string, EbTableBuffer>();
        Dictionary<string, Dictionary<int, EbData>> MapData = new Dictionary<string, Dictionary<int, EbData>>();
        ISqlite Sqlite;
        EbFileStream FileStream = new EbFileStreamDefault();
        Queue<string> QueLoadTbName { get; set; }
        Action<int, int> UpdateCallBack { get; set; }
        Action FinishedCallBack { get; set; }
        int TotalTbCount { get; set; }

        //---------------------------------------------------------------------
        public EbDataMgr()
        {
            QueLoadTbName = new Queue<string>();
        }

        //---------------------------------------------------------------------
        public void Setup(string db_filename, Action<int, int> update_callback, Action finished_callback)
        {
            UpdateCallBack = update_callback;
            FinishedCallBack = finished_callback;

#if UNITY_IPHONE || UNITY_STANDALONE_OSX || UNITY_DASHBOARD_WIDGET || UNITY_STANDALONE_LINUX || UNITY_WEBPLAYER
            Sqlite = new SqliteUnity(db_filename);
#else
            Sqlite = new SqliteWin();
#endif
            if (!Sqlite.openDb(db_filename))
            {
                EbLog.Note("EbDataMgr.setup() failed! Can not Open File! db_filename=" + db_filename);
                return;
            }

            try
            {
                // 加载所有Table数据
                HashSet<string> list_tablename = _loadAllTableName();
                foreach (var i in list_tablename)
                {
                    QueLoadTbName.Enqueue(i);
                }
                TotalTbCount = QueLoadTbName.Count;
            }
            catch (Exception e)
            {
                EbLog.Note(e.ToString());
            }
        }

        //---------------------------------------------------------------------
        public void Setup(string db_filename)
        {
#if UNITY_IPHONE || UNITY_STANDALONE_OSX || UNITY_DASHBOARD_WIDGET || UNITY_STANDALONE_LINUX || UNITY_WEBPLAYER
            Sqlite = new SqliteUnity(db_filename);
#else
            Sqlite = new SqliteWin();
#endif
            if (!Sqlite.openDb(db_filename))
            {
                EbLog.Note("EbDataMgr.setup() failed! Can not Open File! db_filename=" + db_filename);
                return;
            }

            try
            {
                // 加载所有Table数据
                HashSet<string> list_tablename = _loadAllTableName();
                foreach (var i in list_tablename)
                {
                    _loadTable(i);
                }

                Sqlite.closeDb();
            }
            catch (Exception e)
            {
                EbLog.Note(e.ToString());
            }
        }

        //---------------------------------------------------------------------
        public Dictionary<string, byte[]> Setup(string db_name, byte[] db_data)
        {
            Dictionary<string, byte[]> map_table = new Dictionary<string, byte[]>();

#if UNITY_IPHONE || UNITY_STANDALONE_OSX || UNITY_DASHBOARD_WIDGET || UNITY_STANDALONE_LINUX || UNITY_WEBPLAYER
            Sqlite = new SqliteUnity(db_filename);
#else
            Sqlite = new SqliteWin();
#endif

            MemoryStream ms = new MemoryStream(db_data);
            if (!Sqlite.openDb(db_name, ms))
            {
                EbLog.Note("EbDataMgr.setup() failed! Can not Open Stream!");
                ms.Close();
                return map_table;
            }

            // 加载所有Table数据
            HashSet<string> list_tablename = _loadAllTableName();
            foreach (var i in list_tablename)
            {
                _loadTable(i);
                map_table[i] = GetTableAsBytes(i);
            }

            Sqlite.closeDb();
            ms.Close();

            return map_table;
        }

        //---------------------------------------------------------------------
        public void Close()
        {
            foreach (var i in mMapTable)
            {
                i.Value.Close();
            }
            mMapTable.Clear();
        }

        //---------------------------------------------------------------------
        public void Update(float tm)
        {
            try
            {
                if (QueLoadTbName.Count > 0)
                {
                    var tb_name = QueLoadTbName.Dequeue();
                    _loadTable(tb_name);
                    UpdateCallBack(TotalTbCount - QueLoadTbName.Count, TotalTbCount);
                }
                else
                {
                    if (FinishedCallBack != null)
                    {
                        Sqlite.closeDb();
                        var call_back = FinishedCallBack;
                        FinishedCallBack = null;
                        call_back();
                    }
                }
            }
            catch (Exception e)
            {
                EbLog.Note(e.ToString());
            }
        }

        //---------------------------------------------------------------------
        public EbTableBuffer GetTable(string table_name)
        {
            EbTableBuffer table = null;
            mMapTable.TryGetValue(table_name, out table);
            if (table == null)
            {
                EbLog.Error("EbDb.getTable() Error! not exist table,table_name=" + table_name);
            }
            return table;
        }

        //---------------------------------------------------------------------
        public byte[] GetTableAsBytes(string table_name)
        {
            EbTableBuffer table = null;
            mMapTable.TryGetValue(table_name, out table);
            if (table == null)
            {
                EbLog.Error("EbDb.getTable() Error! not exist table,table_name=" + table_name);
            }
            return table.GetTableData();
        }

        //---------------------------------------------------------------------
        public Dictionary<string, byte[]> GetAllTableAsBytes()
        {
            Dictionary<string, byte[]> m = new Dictionary<string, byte[]>();
            foreach (var i in mMapTable)
            {
                m[i.Key] = i.Value.GetTableData();
            }
            return m;
        }

        //---------------------------------------------------------------------
        public void ParseTableAllData<T>(string table_name) where T : EbData, new()
        {
            string key = typeof(T).Name;
            Dictionary<int, EbData> map_data = new Dictionary<int, EbData>();
            MapData[key] = map_data;

            EbTableBuffer table = GetTable(table_name);
            while (!table.IsReadEnd())
            {
                T data = new T();
                data.Id = table.ReadInt();
                data.load(table);
                map_data[data.Id] = data;
            }
        }

        //---------------------------------------------------------------------
        public void ParseTableFromBytes<T>(string table_name, byte[] table_buf) where T : EbData, new()
        {
            EbTableBuffer table = new EbTableBuffer(table_buf, table_name);
            mMapTable[table.TableName] = table;

            string key = typeof(T).Name;
            Dictionary<int, EbData> map_data = new Dictionary<int, EbData>();
            MapData[key] = map_data;

            while (!table.IsReadEnd())
            {
                T data = new T();
                data.Id = table.ReadInt();
                data.load(table);
                map_data[data.Id] = data;
            }
        }

        //---------------------------------------------------------------------
        public void ParseTableFromBytes(Type t, string table_name, byte[] table_buf)
        {
            EbTableBuffer table = new EbTableBuffer(table_buf, table_name);
            mMapTable[table.TableName] = table;

            Dictionary<int, EbData> map_data = new Dictionary<int, EbData>();
            MapData[t.Name] = map_data;

            while (!table.IsReadEnd())
            {
                var data = (EbData)Activator.CreateInstance(t);
                data.Id = table.ReadInt();
                data.load(table);
                map_data[data.Id] = data;
            }
        }

        //---------------------------------------------------------------------
        public T GetData<T>(int id) where T : EbData
        {
            string key = typeof(T).Name;
            Dictionary<int, EbData> map_data = null;

            MapData.TryGetValue(key, out map_data);
            if (map_data == null)
            {
                throw new Exception();
            }

            EbData data = null;
            map_data.TryGetValue(id, out data);
            if (data == null) return default(T);
            else return (T)data;
        }

        //---------------------------------------------------------------------
        public Dictionary<int, EbData> GetMapData<T>() where T : EbData
        {
            string key = typeof(T).Name;
            Dictionary<int, EbData> map_data = null;

            MapData.TryGetValue(key, out map_data);
            return map_data;
        }

        //---------------------------------------------------------------------
        // 获取Db中所有表名
        HashSet<string> _loadAllTableName()
        {
            HashSet<string> list_tablename = new HashSet<string>();
            string str_query = string.Format("SELECT * FROM {0};", "sqlite_master");
            list_tablename = Sqlite.getAllTableName(str_query);
            return list_tablename;
        }

        //---------------------------------------------------------------------
        void _loadTable(string table_name)
        {
            string str_query_select = string.Format("SELECT * FROM {0};", table_name);
            try
            {
                Dictionary<int, List<DataInfo>> map_data = Sqlite.getTableData(str_query_select);
                if (map_data.Count <= 0)
                {
                    return;
                }

                EbTableBuffer table = new EbTableBuffer(table_name);

                foreach (var i in map_data)
                {
                    //int data_id = i.Key;
                    //table.WriteInt(data_id);

                    List<DataInfo> list_data_info = i.Value;
                    foreach (var data_info in list_data_info)
                    {
                        object data_value = data_info.data_value;
                        string data_name = data_info.data_name;

                        switch (data_info.data_type)
                        {
                            case 1:
                                table.WriteInt((int)data_value);
                                break;
                            case 2:
                                table.WriteFloat((float)(double)data_value);
                                break;
                            case 3:
                                table.WriteString((string)data_value);
                                break;
                        }
                    }
                }

                table.WriteEnd();

                mMapTable[table.TableName] = table;
            }
            catch (Exception e)
            {
                EbLog.Error(str_query_select + "_______" + e.ToString());
            }
        }
    }
}
