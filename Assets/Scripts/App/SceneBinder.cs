using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Karaoke.App
{
    /// <summary>
    /// Liga o codigo aos objetos que ja existem na cena, procurando por nome.
    ///
    /// A busca e recursiva de proposito: assim reorganizar a hierarquia no
    /// editor (mover um objeto para dentro de outro, agrupar, renomear o pai)
    /// nao quebra o jogo — so o NOME do objeto importa.
    ///
    /// Tudo que falta e acumulado e reportado de uma vez no fim, com o nome
    /// exato esperado, em vez de estourar uma excecao no primeiro que faltar.
    /// </summary>
    public class SceneBinder
    {
        readonly Transform root;
        readonly List<string> missing = new List<string>();

        public SceneBinder(Transform root)
        {
            this.root = root;
        }

        public bool HasErrors => missing.Count > 0;

        /// <summary>Procura um filho por nome, em qualquer profundidade.</summary>
        public static Transform Find(Transform parent, string name)
        {
            if (parent == null) return null;
            if (parent.name == name) return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = Find(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Objeto obrigatorio: se faltar, entra no relatorio de erros.</summary>
        public Transform Require(string name)
        {
            Transform found = Find(root, name);
            if (found == null) missing.Add(name);
            return found;
        }

        /// <summary>Componente obrigatorio no objeto de nome informado.</summary>
        public T Require<T>(string name) where T : Component
        {
            Transform found = Find(root, name);
            if (found == null)
            {
                missing.Add(name);
                return null;
            }

            T component = found.GetComponent<T>();
            if (component == null) missing.Add(name + " (falta o componente " + typeof(T).Name + ")");
            return component;
        }

        /// <summary>Objeto opcional: ausencia nao e erro.</summary>
        public Transform Optional(string name)
        {
            return Find(root, name);
        }

        public T Optional<T>(string name) where T : Component
        {
            Transform found = Find(root, name);
            return found != null ? found.GetComponent<T>() : null;
        }

        /// <summary>Loga tudo que faltou. Retorna true quando esta tudo no lugar.</summary>
        public bool Report(string context)
        {
            if (missing.Count == 0) return true;

            var sb = new StringBuilder();
            sb.AppendLine("[Karaoke] " + context + ": nao encontrei " + missing.Count + " objeto(s) na cena.");
            foreach (string name in missing) sb.AppendLine("   - " + name);
            sb.AppendLine("Crie com esses nomes exatos dentro do Canvas (a hierarquia pode ser a que voce quiser).");
            Debug.LogError(sb.ToString());
            return false;
        }
    }
}
