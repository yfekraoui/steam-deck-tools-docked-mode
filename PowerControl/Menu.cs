﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
using System.Windows.Forms.VisualStyles;
using System.Xml.Schema;

namespace PowerControl
{
    internal class Menu
    {
        public static readonly String[] Helpers =
        {
            "<C0=008040><C1=0080C0><C2=C08080><C3=FF0000><C4=FFFFFF><C250=FF8000>",
            "<A0=-4><A1=5><A2=-2><A5=-5><S0=-50><S1=50>",
        };

        public enum Colors : int
        { 
            Green,
            Blue,
            Redish,
            Red,
            White
        }

        public abstract class MenuItem
        {
            public String Name { get; set; }
            public bool Visible { get; set; } = true;
            public bool Selectable { get; set; }

            protected string Color(String text, Colors index)
            {
                return String.Format("<C{1}>{0}<C>", text, (int)index);
            }

            public abstract string Render(MenuItem selected);

            public abstract void CreateMenu(ToolStripItemCollection collection);
            public abstract void Update();

            public abstract void SelectNext();
            public abstract void SelectPrev();
        };

        public class MenuItemWithOptions : MenuItem
        {
            public delegate object CurrentValueDelegate();
            public delegate object[] OptionsValueDelegate();
            public delegate object ApplyValueDelegate(object selected);

            public IList<Object> Options { get; set; } = new List<Object>();
            public Object SelectedOption { get; set; }
            public Object ActiveOption { get; set; }
            public int ApplyDelay { get; set; }

            public CurrentValueDelegate CurrentValue { get; set; }
            public OptionsValueDelegate OptionsValues { get; set; }
            public ApplyValueDelegate ApplyValue { get; set; }

            private System.Windows.Forms.Timer delayTimer;
            private ToolStripMenuItem toolStripItem;

            public MenuItemWithOptions()
            {
                this.Selectable = true;
            }
            public override void Update()
            {
                if (CurrentValue != null)
                {
                    var result = CurrentValue();
                    if (result != null)
                        ActiveOption = result;
                }

                if (OptionsValues != null)
                {
                    var result = OptionsValues();
                    if (result != null)
                    {
                        Options = result.ToList();
                        updateOptions();
                    }
                }

                if (ActiveOption == null)
                    ActiveOption = Options.First();
                if (SelectedOption == null)
                    SelectedOption = ActiveOption;

                onUpdateToolStrip();
            }
            
            private void scheduleApply()
            {
                if (delayTimer != null)
                    delayTimer.Stop();

                if (ApplyDelay == 0)
                {
                    onApply();
                    return;
                }

                delayTimer = new System.Windows.Forms.Timer();
                delayTimer.Interval = ApplyDelay > 0 ? ApplyDelay : 1;
                delayTimer.Tick += delegate (object? sender, EventArgs e)
                {
                    if (delayTimer != null)
                        delayTimer.Stop();

                    onApply();
                };
                delayTimer.Enabled = true;
            }

            private void onApply()
            {
                if (ApplyValue != null)
                    ActiveOption = ApplyValue(SelectedOption);

                SelectedOption = ActiveOption;

                onUpdateToolStrip();
            }

            private void onUpdateToolStrip()
            {
                if (toolStripItem == null)
                    return;

                foreach (ToolStripMenuItem item in toolStripItem.DropDownItems)
                    item.Checked = (item.Tag.ToString() == SelectedOption.ToString());

                toolStripItem.Visible = Visible && Options.Count > 0;
            }

            private void updateOptions()
            {
                if (toolStripItem == null)
                    return;

                toolStripItem.DropDownItems.Clear();

                foreach (var option in Options)
                {
                    var optionItem = new ToolStripMenuItem(option.ToString());
                    optionItem.Tag = option;
                    optionItem.Click += delegate (object? sender, EventArgs e)
                    {
                        SelectedOption = option;
                        onApply();
                    };
                    toolStripItem.DropDownItems.Add(optionItem);
                }
            }

            public override void CreateMenu(ToolStripItemCollection collection)
            {
                if (toolStripItem != null)
                    return;

                toolStripItem = new ToolStripMenuItem();
                toolStripItem.Text = Name;
                updateOptions();
                collection.Add(toolStripItem);
            }

            public override void SelectNext()
            {
                int index = Options.IndexOf(SelectedOption);
                if (index >= 0)
                    SelectedOption = Options[Math.Min(index + 1, Options.Count - 1)];
                else
                    SelectedOption = Options.First();

                scheduleApply();
            }

            public override void SelectPrev()
            {
                int index = Options.IndexOf(SelectedOption);
                if (index >= 0)
                    SelectedOption = Options[Math.Max(index - 1, 0)];
                else
                    SelectedOption = Options.First();

                scheduleApply();
            }

            private String optionText(Object option)
            {
                String text;
                
                if (option == null)
                    text = Color("?", Colors.White);
                else if (Object.Equals(option, SelectedOption))
                    text = Color(option.ToString(), Colors.Red);
                else if(Object.Equals(option, ActiveOption))
                    text = Color(option.ToString(), Colors.White);
                else
                    text = Color(option.ToString(), Colors.Green);

                return text;
            }

            public override string Render(MenuItem selected)
            {
                string output = "";

                if (selected == this)
                    output += Color(Name + ":", Colors.White).PadRight(30);
                else
                    output += Color(Name + ":", Colors.Blue).PadRight(30);

                output += optionText(SelectedOption);

                if (!Object.Equals(ActiveOption, SelectedOption))
                    output += " (active: " + optionText(ActiveOption) + ")";

                return output;
            }
        }

        public class MenuRoot : MenuItem
        {
            public IList<MenuItem> Items { get; set; } = new List<MenuItem>();

            public MenuItem Selected;

            public delegate void VisibleChangedDelegate();
            public VisibleChangedDelegate? VisibleChanged;

            public override void CreateMenu(ToolStripItemCollection collection)
            {
                foreach(var item in Items)
                {
                    item.CreateMenu(collection);
                }
            }
            public override void Update()
            {
                foreach (var item in Items)
                {
                    item.Update();
                }
            }

            public override string Render(MenuItem parentSelected)
            {
                var sb = new StringBuilder();

                sb.AppendJoin("", Helpers);
                if (Name != "")
                    sb.AppendLine(Color(Name, Colors.Blue));

                foreach (var item in Items)
                {
                    if (!item.Visible)
                        continue;
                    var lines = item.Render(Selected).Split("\r\n").Select(line => "  " + line);
                    foreach (var line in lines)
                        sb.AppendLine(line);
                }

                return sb.ToString();
            }

            public bool Show()
            {
                if (Visible)
                    return false;

                Visible = true;
                Update();

                if (VisibleChanged != null)
                    VisibleChanged();
                return true;
            }

            public void Prev()
            {
                if (Show())
                    return;

                int index = Items.IndexOf(Selected);

                for (int i = 0; i < Items.Count; i++)
                {
                    index = (index - 1 + Items.Count) % Items.Count;
                    var item = Items[index];
                    if (item.Visible && item.Selectable) {
                        Selected = item;
                        if (VisibleChanged != null)
                            VisibleChanged();
                        return;
                    }
                }
            }

            public void Next()
            {
                if (Show())
                    return;

                int index = Items.IndexOf(Selected);

                for (int i = 0; i < Items.Count; i++)
                {
                    index = (index + 1) % Items.Count;
                    var item = Items[index];
                    if (item.Visible && item.Selectable)
                    {
                        Selected = item;
                        if (VisibleChanged != null)
                            VisibleChanged();
                        return;
                    }
                }
            }

            public override void SelectNext()
            {
                if (Show())
                    return;

                if (Selected != null)
                {
                    Selected.SelectNext();
                    if (VisibleChanged != null)
                        VisibleChanged();
                }
            }

            public override void SelectPrev()
            {
                if (Show())
                    return;

                if (Selected != null)
                {
                    Selected.SelectPrev();
                    if (VisibleChanged != null)
                        VisibleChanged();
                }
            }
        }
    }
}