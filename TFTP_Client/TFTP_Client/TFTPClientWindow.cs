﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;

namespace TFTP_Client
{
    public partial class TFTPClientWindow : Form
    {
        public TFTPClientWindow()
        {
            InitializeComponent(); 
            
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string p = string.Empty;

            OpenFileDialog openFileDialog1 = new OpenFileDialog();
            openFileDialog1.Filter = "All files (*.*)|*.*";

            if (openFileDialog1.ShowDialog() == DialogResult.OK)
                p = openFileDialog1.FileName;

            textBox1.Text = p;
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void groupBox1_Enter(object sender, EventArgs e)
        {

        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {

        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void button2_Click(object sender, EventArgs e)
        {
            upload();
            
            
        }

        private void upload() {
            Client.getInstance().put(textBox1.Text, textBoxIP.Text, textBoxPort.Text); 
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void button3_Click(object sender, EventArgs e)
        {
            Client.getInstance().setRetrPath(textBox3.Text);
            Client.getInstance().get(textBox2.Text, textBoxIP.Text, textBoxPort.Text); 
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {

        }

        
    }
}