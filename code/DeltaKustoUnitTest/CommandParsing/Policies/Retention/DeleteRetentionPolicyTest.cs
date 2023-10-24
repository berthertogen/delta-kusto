using DeltaKustoLib.CommandModel;
using DeltaKustoLib.CommandModel.Policies;
using DeltaKustoLib.CommandModel.Policies.Retention;
using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace DeltaKustoUnitTest.CommandParsing.Policies.Retention
{
    public class DeleteRetentionPolicyTest : ParsingTestBase
    {
        [Fact]
        public void SimpleTable()
        {
            TestRetentionPolicy(EntityType.Table, "A");
        }

        [Fact]
        public void FunkyTable()
        {
            TestRetentionPolicy(EntityType.Table, "A- 1");
        }

        [Fact]
        public void SimpleDatabase()
        {
            TestRetentionPolicy(EntityType.Database, "Db");
        }

        [Fact]
        public void FunkyDatabase()
        {
            TestRetentionPolicy(EntityType.Database, "db.mine");
        }

        private void TestRetentionPolicy(EntityType type, string name)
        {
            var commandText = new DeleteRetentionPolicyCommand(type, new EntityName(name))
                .ToScript(null);
            var command = ParseOneCommand(commandText);

            Assert.IsType<DeleteRetentionPolicyCommand>(command);
        }
    }
}