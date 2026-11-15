// Readonly visible native text inventory. Does not walk/scroll virtualized rows,
// register commands, or modify production body/value/command registries.
module.exports=mvid=>{
 if(!/^[0-9a-f-]{36}$/i.test(mvid))throw Error('Actual active plugin MVID required');
 return `{foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}"){
 var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
 var root=(UnityEngine.UIElements.VisualElement)a.GetType("FM27Access.Plugin.FM27PanelCatalog").GetMethod("GetForegroundPanel",f).Invoke(null,null);
 var visible=a.GetType("FM27Access.Plugin.ElementBridge").GetMethod("IsVisibleInHierarchy",f);
 var textType=typeof(UnityEngine.UIElements.VisualElement).Assembly.GetType("UnityEngine.UIElements.TextElement");
 System.Reflection.MethodInfo cast=null;foreach(var method in typeof(UnityEngine.UIElements.VisualElement).GetMethods())if(method.Name=="TryCast"&&method.IsGenericMethodDefinition&&method.GetParameters().Length==0){cast=method.MakeGenericMethod(new[]{textType});break;}
 var stack=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();stack.Push(root);
 System.Console.WriteLine("ROOT|"+root.name);
 while(stack.Count>0){var e=stack.Pop();if(!(bool)visible.Invoke(null,new object[]{e}))continue;
 var text=cast.Invoke(e,null);if(text!=null){var value=(string)textType.GetProperty("text",f).GetValue(text,null);
 if(!string.IsNullOrWhiteSpace(value)){var path=e.name??"";for(var p=e.parent;p!=null&&p.Pointer!=root.Pointer;p=p.parent)path=(p.name??"")+"/"+path;
 System.Console.WriteLine("TEXT|"+e.Pointer+"|"+e.name+"|"+System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))+"|"+path);}}
 for(int z=e.hierarchy.childCount-1;z>=0;z--)stack.Push(e.hierarchy[z]);
 }
}}`;
};
