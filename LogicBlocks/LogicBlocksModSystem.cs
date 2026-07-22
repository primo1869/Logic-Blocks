using LogicBlocks.Blocks;
using LogicBlocks.Items;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace LogicBlocks
{
    public class LogicBlocksModSystem : ModSystem
    {

        private ICoreAPI? api;
        private readonly Dictionary<String, MeshRef> meshes = [];

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            this.api.RegisterItemClass(this.Mod.Info.ModID + ".connector", typeof(Connector));
            this.api.RegisterBlockEntityClass(this.Mod.Info.ModID + ".pulse", typeof(Pulse));
            this.api.RegisterBlockEntityClass(this.Mod.Info.ModID + ".switch", typeof(LogicBlocks.Blocks.Switch));
            this.api.RegisterBlockEntityClass(this.Mod.Info.ModID + ".andgate", typeof(And));
            this.api.RegisterBlockEntityClass(this.Mod.Info.ModID + ".orgate", typeof(Or));
            this.api.RegisterBlockEntityClass(this.Mod.Info.ModID + ".notgate", typeof(Not));
            this.api.RegisterBlockEntityClass(this.Mod.Info.ModID + ".flipflop", typeof(FlipFlop));
        }

        internal MeshRef UploadMesh(string key)
        {
            if (this.api is not ICoreClientAPI capi)
                throw new InvalidOperationException();

            if (!this.meshes.TryGetValue(key, out MeshRef? value))
            {
                Block? block = capi.World.GetBlock(new AssetLocation(key));
                if (block is null)
                    throw new InvalidOperationException($"Block not found: {key}");
                capi.Tesselator.TesselateBlock(block, out MeshData mesh);
                value = capi.Render.UploadMesh(mesh);
                this.meshes[key] = value;
            }

            return value;
        }

        public override void Dispose()
        {
            if (this.api is not ICoreClientAPI capi)
                return;
            foreach (var var in this.meshes)
                capi.Render.DeleteMesh(var.Value);
        }
    }
}
