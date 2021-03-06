﻿using Aoite.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace Aoite.CommandModel
{
    /// <summary>
    /// 表示命令模型的测试基类。
    /// </summary>
    public abstract class TestBase : ITestMethodHandler
    {
        private TestManagerBase _testManager;
        private ExecutorFactory _factory;

        private DbEngineManager _Manager;
        /// <summary>
        /// 获取或设置当前运行环境的数据库操作引擎集合管理器。
        /// </summary>
        public DbEngineManager Manager
        {
            get { return this._Manager; }
            set
            {
                this._Manager = value;
                this._Engine = value["Default"];
                this._Readonly = value["Readonly"];
            }
        }

        private DbEngine _Engine;
        /// <summary>
        /// 获取当前运行环境的数据库操作引擎的实例。
        /// </summary>
        public DbEngine Engine { get { return this._Engine; } }

        /// <summary>
        /// 获取当前运行环境的数据库操作引擎的实例。
        /// </summary>
        public DbContext Context { get { return this._Engine == null ? null : this._Engine.Context; } }

        private DbEngine _Readonly;
        /// <summary>
        /// 获取当前运行环境的数据库操作引擎的只读实例。
        /// </summary>
        public DbEngine Readonly { get { return this._Readonly; } }

        /// <summary>
        /// 获取一个值，指示当前上下文在线程中是否已创建。
        /// </summary>
        public bool IsThreadContext { get { return this._Engine != null && this._Engine.IsThreadContext; } }

        /// <summary>
        /// 创建命令模型的模拟上下文。
        /// </summary>
        /// <param name="user">模拟的登录用户。</param>
        /// <param name="command">命令模型。</param>
        /// <returns>返回命令模型的模拟上下文。</returns>
        public MockContext CreateContext(object user, ICommand command)
        {
            return new MockContext(user, this._factory.Container, command);
        }

        /// <summary>
        /// 执行一个命令模型。
        /// </summary>
        /// <typeparam name="TCommand">命令模型的数据类型。</typeparam>
        /// <param name="command">命令模型。</param>
        /// <param name="user">模拟的登录用户。</param>
        /// <returns>返回命令模型。</returns>
        public TCommand Execute<TCommand>(TCommand command, object user = null) where TCommand : ICommand
        {
            var executor = this._factory.Create<TCommand>(command);
            executor.Executor.Execute(this.CreateContext(user, command), command);
            return command;
        }

        /// <summary>
        /// 往数据库添加一个模拟的数据行。
        /// </summary>
        /// <typeparam name="TModel">数据表的实体类型。</typeparam>
        /// <param name="callback">添加前的回调函数。</param>
        /// <returns>返回添加的实体。</returns>
        public TModel AddMockModel<TModel>(Action<TModel> callback = null, Action<TModel> after = null)
        {
            return AddMockModels<TModel>(1, callback, after)[0];
        }
        /// <summary>
        /// 往数据库添加一系列模拟的数据行。
        /// </summary>
        /// <typeparam name="TModel">数据表的实体类型。</typeparam>
        /// <param name="length">添加的行数。</param>
        /// <param name="before">添加前的回调函数。</param>
        /// <param name="after">添加后的回调函数。</param>
        /// <returns>返回添加的实体列表。</returns>
        public TModel[] AddMockModels<TModel>(int length = 1, Action<TModel> before = null, Action<TModel> after = null)
        {
            if(length < 1) throw new ArgumentOutOfRangeException("length");
            TModel[] models = new TModel[length];
            using(var context = this.Context)
            {
                for(int i = 0; i < length; i++)
                {
                    var model = GA.CreateMockModel<TModel>();
                    if(before != null) before(model);
                    context.Add(model).ThrowIfFailded();
                    if(after != null) after(model);
                    models[i] = model;
                }
            }

            return models;
        }

        /// <summary>
        /// 返回一个指定范围内的随机数。
        /// </summary>
        /// <param name="min">返回的随机数的下界（随机数可取该下界值）。</param>
        /// <param name="max">返回的随机数的上界（随机数可取该上界值）。</param>
        public int GetRandomNumber(int min, int max)
        {
            return FastRandom.Instance.Next(min, max);
        }

        /// <summary>
        /// 获取一个随机的数字。
        /// </summary>
        /// <returns>返回一个随机数字。</returns>
        public int GetRandomNumber()
        {
            return FastRandom.Instance.Next();
        }

        void ITestMethodHandler.Start()
        {
            var container = new IocContainer();
            this._factory = new ExecutorFactory(container);
            var methodInfo = TestContext.MethodInfo.MethodInfo;
            var db = methodInfo.GetAttribute<DbAttribute>();
            var provider = db == null ? DbEngineProvider.MicrosoftSqlServerCompact : db.Provider;
            switch(provider)
            {
                case DbEngineProvider.MicrosoftSqlServer:
                    this._testManager = new MsSqlTestManager();
                    break;
                default:
                    this._testManager = new MsCeTestManager();
                    break;
            }
            Manager = this._testManager.Manager;
            container.AddService(this.Manager);
            container.AddService<IDbEngine>(lmps => this.Context);

            var methodScripts = methodInfo.GetAttribute<ScriptsAttribute>();
            if(methodScripts != null) this._testManager.Execute(methodScripts.Keys);
            else
            {
                var classScripts = this.GetType().GetAttribute<ScriptsAttribute>();
                if(classScripts != null) this._testManager.Execute(classScripts.Keys);
            }

        }

        void ITestMethodHandler.Finish()
        {
            if(this._Engine != null) this._Engine.ResetContext();
            this._factory = null;
            this._testManager.Dispose();
        }
    }

    /// <summary>
    /// 表示选择数据源查询与交互引擎的提供程序的特性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class DbAttribute : Attribute
    {
        private DbEngineProvider _Provider;
        /// <summary>
        /// 获取数据源查询与交互引擎的提供程序。
        /// </summary>
        public DbEngineProvider Provider { get { return _Provider; } }

        /// <summary>
        /// 初始化 <see cref="DbAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="provider">数据源查询与交互引擎的提供程序。</param>
        public DbAttribute(DbEngineProvider provider)
        {
            this._Provider = provider;
        }
    }

    /// <summary>
    /// 表示自动执行脚本的特性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class ScriptsAttribute : Attribute
    {
        private string[] _Keys;
        /// <summary>
        /// 获取脚本的键名列表。
        /// </summary>
        public string[] Keys { get { return _Keys; } }

        /// <summary>
        /// 初始化 <see cref="ScriptsAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="keys">脚本的键名列表。</param>
        public ScriptsAttribute(params string[] keys)
        {
            if(keys == null) throw new ArgumentNullException("keys");
            this._Keys = keys;
        }
    }
}
