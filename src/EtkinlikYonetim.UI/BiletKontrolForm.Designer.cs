namespace EtkinlikYonetim.UI
{
    partial class BiletKontrolForm
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
            this.lblEtkinlik = new System.Windows.Forms.Label();
            this.cmbEtkinlik = new System.Windows.Forms.ComboBox();
            this.lblSayac = new System.Windows.Forms.Label();
            this.lblKod = new System.Windows.Forms.Label();
            this.txtKod = new System.Windows.Forms.TextBox();
            this.btnKontrol = new System.Windows.Forms.Button();
            this.lblSonuc = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // lblEtkinlik
            // 
            this.lblEtkinlik.AutoSize = true;
            this.lblEtkinlik.Location = new System.Drawing.Point(12, 16);
            this.lblEtkinlik.Name = "lblEtkinlik";
            this.lblEtkinlik.Size = new System.Drawing.Size(51, 16);
            this.lblEtkinlik.TabIndex = 0;
            this.lblEtkinlik.Text = "Etkinlik";
            // 
            // cmbEtkinlik
            // 
            this.cmbEtkinlik.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbEtkinlik.FormattingEnabled = true;
            this.cmbEtkinlik.Location = new System.Drawing.Point(100, 12);
            this.cmbEtkinlik.Name = "cmbEtkinlik";
            this.cmbEtkinlik.Size = new System.Drawing.Size(390, 24);
            this.cmbEtkinlik.TabIndex = 1;
            this.cmbEtkinlik.SelectedIndexChanged += new System.EventHandler(this.cmbEtkinlik_SelectedIndexChanged);
            // 
            // lblSayac
            // 
            this.lblSayac.Location = new System.Drawing.Point(100, 44);
            this.lblSayac.Name = "lblSayac";
            this.lblSayac.Size = new System.Drawing.Size(390, 20);
            this.lblSayac.TabIndex = 2;
            // 
            // lblKod
            // 
            this.lblKod.AutoSize = true;
            this.lblKod.Location = new System.Drawing.Point(12, 84);
            this.lblKod.Name = "lblKod";
            this.lblKod.Size = new System.Drawing.Size(70, 16);
            this.lblKod.TabIndex = 3;
            this.lblKod.Text = "Bilet kodu";
            // 
            // txtKod
            // 
            this.txtKod.Location = new System.Drawing.Point(100, 81);
            this.txtKod.Name = "txtKod";
            this.txtKod.Size = new System.Drawing.Size(280, 22);
            this.txtKod.TabIndex = 4;
            // 
            // btnKontrol
            // 
            this.btnKontrol.Location = new System.Drawing.Point(390, 77);
            this.btnKontrol.Name = "btnKontrol";
            this.btnKontrol.Size = new System.Drawing.Size(100, 30);
            this.btnKontrol.TabIndex = 5;
            this.btnKontrol.Text = "Kontrol Et";
            this.btnKontrol.UseVisualStyleBackColor = true;
            this.btnKontrol.Click += new System.EventHandler(this.btnKontrol_Click);
            // 
            // lblSonuc
            // 
            this.lblSonuc.Font = new System.Drawing.Font("Microsoft Sans Serif", 13F, System.Drawing.FontStyle.Bold);
            this.lblSonuc.Location = new System.Drawing.Point(12, 124);
            this.lblSonuc.Name = "lblSonuc";
            this.lblSonuc.Size = new System.Drawing.Size(478, 64);
            this.lblSonuc.TabIndex = 6;
            this.lblSonuc.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // BiletKontrolForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(504, 204);
            this.Controls.Add(this.lblSonuc);
            this.Controls.Add(this.btnKontrol);
            this.Controls.Add(this.txtKod);
            this.Controls.Add(this.lblKod);
            this.Controls.Add(this.lblSayac);
            this.Controls.Add(this.cmbEtkinlik);
            this.Controls.Add(this.lblEtkinlik);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "BiletKontrolForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Bilet Kontrol";
            this.Load += new System.EventHandler(this.BiletKontrolForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblEtkinlik;
        private System.Windows.Forms.ComboBox cmbEtkinlik;
        private System.Windows.Forms.Label lblSayac;
        private System.Windows.Forms.Label lblKod;
        private System.Windows.Forms.TextBox txtKod;
        private System.Windows.Forms.Button btnKontrol;
        private System.Windows.Forms.Label lblSonuc;
    }
}