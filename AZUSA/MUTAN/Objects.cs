﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AZUSA
{
    partial class MUTAN
    {
        //用來存放執行物件執行後生成的返回碼, AZUSA 內部會根據返回碼作業
        //used to store the result of running a command
        public struct ReturnCode
        {
            public string Command, Argument;

            public ReturnCode(string Cmd, string Arg)
            {
                Command = Cmd;
                Argument = Arg;
            }
        }

        //執行物件是對 MUTAN 語句的一種抽象
        // MUTAN 語句最重要的就是可以被"執行"
        //具體執行的方法每一種語句都有所不同
        //The Runnable interface is a representation of "line" in the definition of MUTAN
        //The only important feature is that it can be "run" since it is a valid syntax
        //The detailed implementation of how to "run" a line is deferred to the object  
        public interface IRunnable
        {
            ReturnCode Run();
        }

        //空白語句的物件
        //empty object for an empty line, or comment
        class empty : IRunnable
        {
            public ReturnCode Run()
            {
                //單純返回空白返回碼
                return new ReturnCode("","");
            }
        }

        // decla 的物件
        //The simple decla statement
        class decla : IRunnable
        {
            //decla 有等號左手邊的 ID 和右手邊的表達式兩部分
            string ID;
            string expr;

            public decla(string line)
            {
                //對字串分割並去掉多餘空白
                ID = line.Split('=')[0];
                expr = line.Replace(ID + "=", "");
                ID = ID.Trim();
            }

            ~decla()
            {
                ID = null;
                expr = null;
            }


            public ReturnCode Run()
            {
                //暫存運算結果
                string val;

                //如果運算成功就寫入變量的值
                //回傳空白的返回碼
                if (ExprParser.TryParse(expr, out val))
                {
                    //如果是反應設定
                    if (ID == "$WAITFORRESP" && val.ToUpper() == "FALSE")
                    {
                        Internals.Execute("MAKERESP", "");
                        return new ReturnCode("", "");
                    }

                    //寫入變量                    
                    Variables.Write(ID, val);

                    return new ReturnCode("","");
                }
                //失敗的話就回傳錯誤信息
                else
                {
                    return new ReturnCode("ERR", expr + Localization.GetMessage("INVALIDEXPR", " is not a valid expression.") + " [MUTAN, " + ID + "=" + expr + "]");
                }
            }
        }

        // exec 的物件
        //The simple exec statement
        class exec : IRunnable
        {
            // exec 有函式名和參數兩部分
            string RID;
            string arg;

            public exec(string line)
            {
                //對字串分割並去掉多餘空白
                RID = line.Split('(')[0];
                arg = line.Substring(RID.Length + 1, line.Length - RID.Length - 2);
                RID = RID.Trim();
            }

            ~exec()
            {
                RID = null;
                arg = null;
            }

            public ReturnCode Run()
            {
                //如果參數是空白,就直接執行, 不必運算參數                
                if (arg.Trim() == "")
                {
                    Internals.Execute(RID, "");
                    return new ReturnCode("", "");
                }

                //否則就要對參數進行運數

                //暫存運算結果
                string val;

                //如果失敗的話就回傳錯誤信息
                if (!ExprParser.TryParse(arg, out val))
                {
                    return new ReturnCode("ERR", arg + Localization.GetMessage("INVALIDEXPR", " is not a valid expression.") + "[MUTAN, " + RID + "(" + arg + ")]");
                }

                //否則就執行
                Internals.Execute(RID, val);
                return new ReturnCode("","");
            }
        }

        //中斷語句的物件
        class term : IRunnable
        {
            string command = "";
            public term(string line)
            {
                if (line == "END")
                {
                    command = "END";
                }
                else
                {
                    command = "BREAK";
                }

            }
            public ReturnCode Run()
            {
                //單純返回指令
                return new ReturnCode(command, "");
            }
        }

        // multi 的物件
        //The multi statement
        class multi : IRunnable
        {
            // multi 是由多個 basic 物件組成的
            IRunnable[] basics;

            public multi(string line)
            {
                //先分割成多個 basic
                string[] parts = line.Split(';');
                basics = new IRunnable[parts.Length];

                //然後就每一個 basic 進行分類
                for (int i = 0; i < parts.Length; i++)
                {
                    if (IsComment(parts[i]))
                    {
                        basics[i] = new empty();
                    }
                    else if (IsTerm(parts[i]))
                    {
                        basics[i] = new term(parts[i].Trim());
                    }
                    else if (IsExec(parts[i]))
                    {
                        basics[i] = new exec(parts[i].Trim());
                    }
                    else
                    {
                        basics[i] = new decla(parts[i].Trim());
                    }
                }

            }

            ~multi()
            {
                basics = null;
            }

            public ReturnCode Run()
            {
                //暫存執行結果
                List<ReturnCode> returns = new List<ReturnCode>();
                ReturnCode tmp;

                foreach (IRunnable basic in basics)
                {
                    //逐句執行
                    tmp = basic.Run();
                    if (tmp.Command != "")
                    {
                        return tmp;
                    }
                    
                }

                return new ReturnCode("", "");
            }
        }

        // cond 的物件
        //The cond statement
        class cond : IRunnable
        {
            //cond 有一個條件和 multi 內容
            string condition;
            multi content;

            public cond(string line)
            {
                //提取條件和 multi
                condition = line.Split('?')[0];
                content = new multi(line.Replace(condition + "?", ""));
            }

            ~cond()
            {
                condition = null;
                content = null;
            }

            public ReturnCode Run()
            {
                //暫存運算結果
                string check;

                //對條件進行運算
                if (ExprParser.TryParse(condition, out check))
                {
                    //暫存轉換結果
                    bool chk;

                    //如果轉換成功, 則按條件的值決定是否執行內容
                    if (Boolean.TryParse(check, out chk))
                    {
                        if (chk)
                        {
                            return content.Run();
                        }
                        else
                        {
                            return new ReturnCode("", "");
                        }
                    }
                    //否則回傳錯誤信息
                    else
                    {
                        return new ReturnCode("ERR", condition + Localization.GetMessage("INVALIDBOOL", " is not a valid boolean.") + " [MUTAN, " + check + "]");
                    }
                }
                //如果不是合法表達式則回傳錯誤信息
                else
                {
                    return new ReturnCode("ERR", condition + Localization.GetMessage("INVALIDEXPR", " is not a valid expression.") + " [MUTAN, " + check + "]");
                }
            }
        }


        // loop 的物件
        //The loop statement
        class loop : IRunnable
        {
            //循環的內容
            string content;

            public loop(string line)
            {
                //把 @ 去掉就是內容的部分了
                content = line.TrimStart('@');
            }

            ~loop()
            {
                content = null;
            }

            public ReturnCode Run()
            {
                //創建循環線程
                ThreadManager.AddLoop(content.Split('\n'));
                return new ReturnCode("", "");
            }
        }

        // resp 的物件
        class resp : IRunnable
        {
            //反應的內容
            string content;

            public resp(string line)
            {
                //把 ? 去掉就是內容的部分了
                content = line.TrimStart('?');
            }

            ~resp()
            {
                content = null;
            }

            public ReturnCode Run()
            {
                //執行 WAITFORRESP 指令, 讓 AZUSA 等待回應然後執行反應
                Internals.Execute("WAITFORRESP", content);
                return new ReturnCode("", "");
            }
        }

        //接下來是區塊的物件

        // namedblcok 的物件
        //命名區塊是不會被運行的, 只有當直接指定某命名區塊時, AZUSA 才會執行其內容
        //所以其實這個物件是不必要, 只是為保持整體的完整性和可讀性而已
        //The named block
        //This definition is not implemented because during actual runtime
        //the content of the block is parsed and executed
        //The definition here is to keep the parser aware of the fact that this
        //is a valid syntax so it doesn't produce error messages
        class namedblock : IRunnable
        {
            //IRunnable[] objects;
            //string ID;

            public namedblock(string[] lines)
            {
                //ID = lines[0].Trim().TrimStart('.').TrimEnd('{').Trim();
                //string[] content = new string[lines.Length - 2];
                //for (int i = 1; i < lines.Length - 1; i++)
                //{
                //    content[i - 1] = lines[i];
                //}
                //objects = ParseBlock(content);
                ////the last line contains only a '}' and can be ignored
            }

            public ReturnCode Run()
            {
                //The block is not executed, it can only be called
                //We include this definition to let parser know it is a valid syntax
                //when executed the content of block is passed directly

                return new ReturnCode("", "");
            }



        }

        // respblock 的物件
        class respblock : IRunnable
        {
            // respblock 有內容
            string[] content;

            public respblock(string[] lines)
            {
                //把頭尾兩行去掉就是內容了
                content = new string[lines.Length - 2];
                for (int i = 1; i < lines.Length - 1; i++)
                {
                    content[i - 1] = lines[i];
                }
            }

            ~respblock()
            {
                content = null;
            }

            public ReturnCode Run()
            {
                //暫存內容
                string arg = "";
                foreach (string line in content)
                {
                    //每一句用 /n 分隔
                    arg += line + "\n";
                }
                //利用 WAITFORRESP 指令等待回應然後執行反應
                Internals.Execute("WAITFORRESP", arg);
                return new ReturnCode("", "");
            }
        }


        // condblcok 的物件
        //The condition block
        class condblock : IRunnable
        {
            //區塊有條件和內容
            string condition;
            IRunnable[] objects;

            public condblock(string[] lines)
            {
                //取得條件
                condition = lines[0].Trim().TrimEnd('{');

                //把頭尾兩行去掉就是內容了
                string[] content = new string[lines.Length - 2];
                for (int i = 1; i < lines.Length - 1; i++)
                {
                    content[i - 1] = lines[i];
                }

                //把內容解析成物件
                objects = ParseBlock(content);

                //the last line contains only a '}' and can be ignored
            }

            ~condblock()
            {
                condition = null;
                objects = null;
            }

            public ReturnCode Run()
            {
                //暫存運算結果
                string check;

                //對條件進行運算
                if (ExprParser.TryParse(condition, out check))
                {
                    //暫存轉換結果
                    bool chk;

                    //對運算結果進行運算
                    if (Boolean.TryParse(check, out chk))
                    {
                        //如果是 true
                        if (chk)
                        {
                            //暫存執行結果
                            List<ReturnCode> returns = new List<ReturnCode>();
                            ReturnCode tmp;

                            //執行內容
                            foreach (IRunnable obj in objects)
                            {
                                tmp = obj.Run();

                                if (tmp.Command != "")
                                {
                                    return tmp;
                                }

                            }
                        }

                        return new ReturnCode("", "");
                        
                    }
                    //如果不是布林值, 回傳錯誤信息
                    else
                    {
                        return new ReturnCode("ERR", condition + Localization.GetMessage("INVALIDBOOL", " is not a valid boolean.") + " [MUTAN, " + check + "]");
                    }
                }
                //如果無法運算, 回傳錯誤信息
                else
                {
                    return new ReturnCode("ERR", condition + Localization.GetMessage("INVALIDEXPR", " is not a valid expression.") + " [MUTAN, " + check + "]");
                }
            }
        }

        // loopblock 的物件
        //The loop block
        class loopblock : IRunnable
        {
            // loopblock 有內容
            string[] content;

            public loopblock(string[] lines)
            {
                //把頭尾兩行去掉就是內容了
                content = new string[lines.Length - 2];
                //the first line is just "@{" and can be ignored
                for (int i = 1; i < lines.Length - 1; i++)
                {
                    content[i - 1] = lines[i];
                }
                //the last line contains only a '}' and can be ignored
            }

            ~loopblock()
            {
                content = null;
            }

            public ReturnCode Run()
            {                
                //創建循環線程
                ThreadManager.AddLoop(content);
                return new ReturnCode("", "");
            }
        }

        // block 的物件
        //The block
        class block : IRunnable
        {
            // block 有內容
            IRunnable[] objects;

            public block(string[] lines)
            {
                //把語句解析成物件
                objects = ParseBlock(lines);
            }

            ~block()
            {
                objects = null;
            }

            public ReturnCode Run()
            {
                //暫存執行結果
                List<ReturnCode> returns = new List<ReturnCode>();
                ReturnCode tmp;

                //執行內容
                foreach (IRunnable obj in objects)
                {
                    tmp = obj.Run();
                    if (tmp.Command != "")
                    {
                        return tmp;
                    }

                }

                return new ReturnCode("", "");
            }

        }


    }
}
