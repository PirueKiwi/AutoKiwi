﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace AutoKiwi
{
    public partial class MainForm : Form
    {
        private SimpleAutoKiwiAPI _api;
        public MainForm()
        {
            InitializeComponent();
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            _api = new SimpleAutoKiwiAPI();
            _ = Task.Run(async () => await _api.StartAsync());
        }
    }


}
