namespace EtkinlikYonetim.UI
{
    partial class KatilimciGoruntule
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.cmbEtkinlikSec = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.lstKatilimcilar = new System.Windows.Forms.ListBox();
            this.btnCsv = new System.Windows.Forms.Button();
            this.btnHtml = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // cmbEtkinlikSec
            // 
            this.cmbEtkinlikSec.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbEtkinlikSec.FormattingEnabled = true;
            this.cmbEtkinlikSec.Location = new System.Drawing.Point(104, 25);
            this.cmbEtkinlikSec.Name = "cmbEtkinlikSec";
            this.cmbEtkinlikSec.Size = new System.Drawing.Size(441, 24);
            this.cmbEtkinlikSec.TabIndex = 0;
            this.cmbEtkinlikSec.SelectedIndexChanged += new System.EventHandler(this.cmbEtkinlikSec_SelectedIndexChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 28);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(86, 16);
            this.label1.TabIndex = 1;
            this.label1.Text = "Etkinlik Seçin";
            // 
            // lstKatilimcilar
            // 
            this.lstKatilimcilar.FormattingEnabled = true;
            this.lstKatilimcilar.HorizontalScrollbar = true;
            this.lstKatilimcilar.IntegralHeight = false;
            this.lstKatilimcilar.ItemHeight = 16;
            this.lstKatilimcilar.Location = new System.Drawing.Point(15, 68);
            this.lstKatilimcilar.Name = "lstKatilimcilar";
            this.lstKatilimcilar.Size = new System.Drawing.Size(530, 260);
            this.lstKatilimcilar.TabIndex = 2;
            // 
            // btnCsv
            // 
            this.btnCsv.Location = new System.Drawing.Point(15, 340);
            this.btnCsv.Name = "btnCsv";
            this.btnCsv.Size = new System.Drawing.Size(200, 36);
            this.btnCsv.TabIndex = 3;
            this.btnCsv.Text = "CSV Olarak Kaydet";
            this.btnCsv.UseVisualStyleBackColor = true;
            this.btnCsv.Click += new System.EventHandler(this.btnCsv_Click);
            // 
            // btnHtml
            // 
            this.btnHtml.Location = new System.Drawing.Point(225, 340);
            this.btnHtml.Name = "btnHtml";
            this.btnHtml.Size = new System.Drawing.Size(240, 36);
            this.btnHtml.TabIndex = 4;
            this.btnHtml.Text = "Yazdırılabilir Liste (HTML)";
            this.btnHtml.UseVisualStyleBackColor = true;
            this.btnHtml.Click += new System.EventHandler(this.btnHtml_Click);
            // 
            // KatilimciGoruntule
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(560, 392);
            this.Controls.Add(this.btnHtml);
            this.Controls.Add(this.btnCsv);
            this.Controls.Add(this.lstKatilimcilar);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.cmbEtkinlikSec);
            this.Name = "KatilimciGoruntule";
            this.Text = "Etkinlik Yönetim - Katılımcı Görüntüle";
            this.Load += new System.EventHandler(this.KatilimciGoruntule_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ComboBox cmbEtkinlikSec;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ListBox lstKatilimcilar;
        private System.Windows.Forms.Button btnCsv;
        private System.Windows.Forms.Button btnHtml;
    }
}
