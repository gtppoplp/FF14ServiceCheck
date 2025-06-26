using System;
using System.Collections.Generic;

namespace ServiceCheck.Models
{
    /// <summary>
    /// ��ʾһ�����������򣬰������������
    /// </summary>
    public class ServerArea
    {
        /// <summary>
        /// ��������
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// �������µķ������б�
        /// </summary>
        public List<Server> Servers { get; set; }
    }
    
    /// <summary>
    /// ��ʾһ��������
    /// </summary>
    public class Server
    {
        /// <summary>
        /// ����������
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// �������Ƿ���������
        /// </summary>
        public bool IsRunning { get; set; }
        
        /// <summary>
        /// �Ƿ�ѡ�н��м��
        /// </summary>
        public bool IsSelected { get; set; }
    }
}