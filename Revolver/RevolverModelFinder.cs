using System.Linq;
using ThunderRoad;
using UnityEngine;

namespace Revolver
{
    /// <summary>
    /// Blade & Sorcery itself has no firearms. If the game version or another
    /// installed mod adds one, give the revolver that model instead of the dagger.
    /// </summary>
    public class RevolverModelFinder : ThunderScript
    {
        private static readonly string[] Keywords =
            { "revolver", "pistol", "gun", "flintlock", "musket", "blunderbuss", "firearm", "crossbow" };

        private bool done;

        public override void ScriptUpdate()
        {
            base.ScriptUpdate();
            if (done) return;
            try
            {
                TrySwapModel();
            }
            catch (System.Exception e)
            {
                // Never break the game's script loop: give up and keep the dagger.
                done = true;
                Debug.LogWarning("[Revolver] Model search failed, keeping the dagger: " + e.Message);
            }
        }

        private void TrySwapModel()
        {
            if (Time.frameCount % 30 != 0) return; // check twice a second, not every frame
            ItemData revolver = Catalog.GetData<ItemData>("Revolver", false);
            if (revolver == null) return; // catalog not loaded yet
            done = true;

            ItemData model = Catalog.GetDataList<ItemData>()
                .Where(d => d != null && d != revolver && !string.IsNullOrEmpty(d.prefabAddress))
                .Where(d => d.type == ItemData.Type.Weapon)
                .Where(d => d.id != null && Keywords.Any(k => d.id.ToLowerInvariant().Contains(k)))
                .Where(d => !d.id.ToLowerInvariant().Contains("proto"))
                .OrderBy(d => Keywords.TakeWhile(k => !d.id.ToLowerInvariant().Contains(k)).Count())
                .FirstOrDefault();

            if (model == null)
            {
                Debug.Log("[Revolver] No firearm model in the game or other mods, keeping the dagger model.");
                return;
            }

            // The transform names in these lists belong to the prefab, so they move together.
            revolver.prefabAddress = model.prefabAddress;
            revolver.iconAddress = model.iconAddress;
            revolver.colliderGroups = model.colliderGroups;
            revolver.damagers = model.damagers;
            revolver.Interactables = model.Interactables;
            revolver.whooshs = model.whooshs;
            Debug.Log("[Revolver] Using the model of '" + model.id + "' (" + model.prefabAddress + ").");
        }
    }
}
