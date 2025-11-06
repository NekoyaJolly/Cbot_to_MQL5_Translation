//+------------------------------------------------------------------+
//|                                                        JAson.mqh |
//|                                      Simplified JSON parser      |
//+------------------------------------------------------------------+
#property copyright "Simplified JSON Parser"
#property link      ""
#property strict

//+------------------------------------------------------------------+
//| JSON Value class                                                  |
//+------------------------------------------------------------------+
class CJAVal
{
private:
    string m_key;
    string m_str_value;
    double m_num_value;
    bool   m_is_string;
    bool   m_is_number;
    bool   m_is_array;
    bool   m_is_object;
    
    CJAVal* m_items[];
    
public:
    CJAVal() : m_key(""), m_str_value(""), m_num_value(0), 
               m_is_string(false), m_is_number(false), 
               m_is_array(false), m_is_object(false)
    {
        ArrayResize(m_items, 0);
    }
    
    ~CJAVal()
    {
        Clear();
    }
    
    void Clear()
    {
        for(int i = 0; i < ArraySize(m_items); i++)
        {
            if(m_items[i] != NULL && CheckPointer(m_items[i]) == POINTER_DYNAMIC)
            {
                delete m_items[i];
                m_items[i] = NULL;
            }
        }
        ArrayResize(m_items, 0);
    }
    
    // Deserialize JSON string
    bool Deserialize(string json)
    {
        Clear();
        json = StringTrimLeft(StringTrimRight(json));
        
        if(StringLen(json) == 0)
            return false;
        
        // Check if it's an array
        if(StringGetCharacter(json, 0) == '[')
        {
            m_is_array = true;
            return ParseArray(json);
        }
        // Check if it's an object
        else if(StringGetCharacter(json, 0) == '{')
        {
            m_is_object = true;
            return ParseObject(json);
        }
        
        return false;
    }
    
    // Parse JSON array
    bool ParseArray(string json)
    {
        if(StringLen(json) < 2)
            return false;
        
        // Remove brackets
        json = StringSubstr(json, 1, StringLen(json) - 2);
        json = StringTrimLeft(StringTrimRight(json));
        
        if(StringLen(json) == 0)
            return true; // Empty array
        
        // Simple parsing - split by commas (not in quotes)
        string items[];
        SplitJSON(json, items);
        
        for(int i = 0; i < ArraySize(items); i++)
        {
            CJAVal* item = new CJAVal();
            if(item.Deserialize(items[i]))
            {
                int size = ArraySize(m_items);
                ArrayResize(m_items, size + 1);
                m_items[size] = item;
            }
            else
            {
                delete item;
            }
        }
        
        return true;
    }
    
    // Parse JSON object
    bool ParseObject(string json)
    {
        if(StringLen(json) < 2)
            return false;
        
        // Remove braces
        json = StringSubstr(json, 1, StringLen(json) - 2);
        json = StringTrimLeft(StringTrimRight(json));
        
        if(StringLen(json) == 0)
            return true; // Empty object
        
        // Split by commas
        string pairs[];
        SplitJSON(json, pairs);
        
        for(int i = 0; i < ArraySize(pairs); i++)
        {
            int colonPos = StringFind(pairs[i], ":");
            if(colonPos > 0)
            {
                string key = StringTrimLeft(StringTrimRight(StringSubstr(pairs[i], 0, colonPos)));
                string value = StringTrimLeft(StringTrimRight(StringSubstr(pairs[i], colonPos + 1)));
                
                // Remove quotes from key
                if(StringGetCharacter(key, 0) == '"')
                    key = StringSubstr(key, 1, StringLen(key) - 2);
                
                CJAVal* item = new CJAVal();
                item.m_key = key;
                
                // Parse value
                if(StringGetCharacter(value, 0) == '"')
                {
                    // String value
                    item.m_str_value = StringSubstr(value, 1, StringLen(value) - 2);
                    item.m_is_string = true;
                }
                else if(value == "null")
                {
                    // Null value
                    item.m_str_value = "";
                    item.m_num_value = 0;
                }
                else if(value == "true" || value == "false")
                {
                    // Boolean value
                    item.m_is_number = true;
                    item.m_num_value = (value == "true") ? 1 : 0;
                }
                else if(StringGetCharacter(value, 0) == '{')
                {
                    // Nested object
                    item.Deserialize(value);
                }
                else if(StringGetCharacter(value, 0) == '[')
                {
                    // Nested array
                    item.Deserialize(value);
                }
                else
                {
                    // Number value
                    item.m_is_number = true;
                    item.m_num_value = StringToDouble(value);
                }
                
                int size = ArraySize(m_items);
                ArrayResize(m_items, size + 1);
                m_items[size] = item;
            }
        }
        
        return true;
    }
    
    // Split JSON by commas (respecting nesting)
    void SplitJSON(string json, string &result[])
    {
        ArrayResize(result, 0);
        
        int depth = 0;
        int start = 0;
        bool inString = false;
        
        for(int i = 0; i < StringLen(json); i++)
        {
            ushort ch = StringGetCharacter(json, i);
            
            if(ch == '"' && (i == 0 || StringGetCharacter(json, i - 1) != '\\'))
            {
                inString = !inString;
            }
            
            if(!inString)
            {
                if(ch == '{' || ch == '[')
                    depth++;
                else if(ch == '}' || ch == ']')
                    depth--;
                else if(ch == ',' && depth == 0)
                {
                    string item = StringSubstr(json, start, i - start);
                    item = StringTrimLeft(StringTrimRight(item));
                    
                    int size = ArraySize(result);
                    ArrayResize(result, size + 1);
                    result[size] = item;
                    
                    start = i + 1;
                }
            }
        }
        
        // Add last item
        if(start < StringLen(json))
        {
            string item = StringSubstr(json, start);
            item = StringTrimLeft(StringTrimRight(item));
            
            int size = ArraySize(result);
            ArrayResize(result, size + 1);
            result[size] = item;
        }
    }
    
    // Get item by index (for arrays)
    CJAVal* operator[](int index)
    {
        if(index >= 0 && index < ArraySize(m_items))
            return m_items[index];
        
        // Return NULL if not found - prevents memory leaks
        return NULL;
    }
    
    // Get item by key (for objects)
    CJAVal* operator[](string key)
    {
        for(int i = 0; i < ArraySize(m_items); i++)
        {
            if(m_items[i] != NULL && m_items[i].m_key == key)
                return m_items[i];
        }
        
        // Return NULL if not found - prevents memory leaks
        return NULL;
    }
    
    // Safe accessor methods to prevent memory leaks
    string GetStringByKey(string key, string defaultValue = "")
    {
        for(int i = 0; i < ArraySize(m_items); i++)
        {
            if(m_items[i] != NULL && m_items[i].m_key == key)
                return m_items[i].ToStr();
        }
        return defaultValue;
    }
    
    double GetDoubleByKey(string key, double defaultValue = 0.0)
    {
        for(int i = 0; i < ArraySize(m_items); i++)
        {
            if(m_items[i] != NULL && m_items[i].m_key == key)
                return m_items[i].ToDbl();
        }
        return defaultValue;
    }
    
    long GetIntByKey(string key, long defaultValue = 0)
    {
        for(int i = 0; i < ArraySize(m_items); i++)
        {
            if(m_items[i] != NULL && m_items[i].m_key == key)
                return m_items[i].ToInt();
        }
        return defaultValue;
    }
    
    bool GetBoolByKey(string key, bool defaultValue = false)
    {
        for(int i = 0; i < ArraySize(m_items); i++)
        {
            if(m_items[i] != NULL && m_items[i].m_key == key)
                return m_items[i].ToBool();
        }
        return defaultValue;
    }
    
    CJAVal* GetArrayItem(int index)
    {
        if(index >= 0 && index < ArraySize(m_items))
            return m_items[index];
        return NULL;
    }
    
    // Get array size
    int Size()
    {
        return ArraySize(m_items);
    }
    
    // Convert to string
    string ToStr()
    {
        return m_str_value;
    }
    
    // Convert to double
    double ToDbl()
    {
        if(m_is_number)
            return m_num_value;
        return StringToDouble(m_str_value);
    }
    
    // Convert to integer
    long ToInt()
    {
        if(m_is_number)
            return (long)m_num_value;
        return (long)StringToInteger(m_str_value);
    }
    
    // Convert to boolean
    bool ToBool()
    {
        if(m_is_number)
            return m_num_value != 0;
        return m_str_value == "true";
    }
};
//+------------------------------------------------------------------+
