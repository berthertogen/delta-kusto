using DeltaKustoLib.CommandModel;
using DeltaKustoLib.CommandModel.Policies;
using DeltaKustoLib.CommandModel.Policies.Caching;
using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace DeltaKustoUnitTest.CommandParsing.Policies.Caching
{
    public class DeleteCachingPolicyTest : ParsingTestBase
    {
        [Fact]
        public void SimpleTable()
        {
            TestDeleteCachingPolicy(EntityType.Table, "A", "3d");
        }

        [Fact]
        public void FunkyTable()
        {
            TestDeleteCachingPolicy(EntityType.Table, "A- 1", "90m");
        }

        [Fact]
        public void SimpleDatabase()
        {
            TestDeleteCachingPolicy(EntityType.Database, "Db", "40s");
        }

        [Fact]
        public void FunkyDatabase()
        {
            TestDeleteCachingPolicy(EntityType.Database, "db.mine", "90h");
        }

        private void TestDeleteCachingPolicy(EntityType type, string name, string duration)
        {
            var commandText = new DeleteCachingPolicyCommand(
                type,
                new EntityName(name))
                .ToScript(null);
            var command = ParseOneCommand(commandText);

            Assert.IsType<DeleteCachingPolicyCommand>(command);
        }
    }
}