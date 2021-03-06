using System;
using System.Collections.Generic;
using System.Data;
using Griffin.Data.Mapper.Values;

namespace Griffin.Data.Mapper.CommandBuilders
{
    /// <summary>
    ///     Base class for command builders
    /// </summary>
    /// <remarks>
    ///     Creates SQL commands per the SQL92 standard. Inherit this class to customize different commands.
    /// </remarks>
    public class CommandBuilder : ICommandBuilder
    {
        private readonly List<IPropertyMapping> _keys = new List<IPropertyMapping>();
        private readonly ICrudEntityMapper _mapper;
        private readonly string _tableName;
        private readonly List<IPropertyMapping> _values = new List<IPropertyMapping>();

        /// <summary>
        ///     Initializes a new instance of the <see cref="CommandBuilder" /> class.
        /// </summary>
        /// <param name="mapper">The mapper.</param>
        /// <exception cref="System.ArgumentNullException">mapper</exception>
        public CommandBuilder(ICrudEntityMapper mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _tableName = mapper.TableName;
            foreach (var property in mapper.Properties.Values)
            {
                if (property.IsPrimaryKey)
                    _keys.Add(property);
                else
                    _values.Add(property);
            }
            TreatZeroAsNullForKeys = true;
        }

        /// <summary>
        ///     Gets table that the mapping is for
        /// </summary>
        public string TableName => _tableName;

        /// <summary>
        ///     Mapper that this builder is for.
        /// </summary>
        protected ICrudEntityMapper Mapper => _mapper;

        /// <summary>
        ///     Use <c>DBNull</c> as value if a primary key is 0.
        /// </summary>
        public bool TreatZeroAsNullForKeys { get; set; }

        /// <summary>
        ///     Gets prefix to use for data parameters (typically '@' or ':')
        /// </summary>
        public virtual char ParameterPrefix => '@';

        /// <summary>
        ///     Generate an insert command, should end with a command that returns the insert identity.
        /// </summary>
        /// <param name="command">Command to add the query to</param>
        /// <param name="entity">Entity to store</param>
        /// <exception cref="System.ArgumentNullException">
        ///     command
        ///     or
        ///     entity
        /// </exception>
        /// <exception cref="MappingException">Could not construct a correct SQL statement.</exception>
        public virtual void InsertCommand(IDbCommand command, object entity)
        {
            if (command == null) throw new ArgumentNullException("command");
            if (entity == null) throw new ArgumentNullException("entity");
            if (_keys.Count == 0)
                throw new MappingException(entity.GetType(), "The mapping has no primary keys defined.");

            var columns = "";
            var values = "";
            foreach (var key in _keys)
            {
                var value = key.GetValue(entity);
                if (value == null || (TreatZeroAsNullForKeys && value.Equals(0)))
                    continue;

                if (key.IsAutoIncrement && value.Equals(0))
                    continue;

                columns += $"{key.ColumnName}, ";
                values += $"@{key.PropertyName}, ";
                command.AddParameter(key.PropertyName, value);
            }
            foreach (var prop in _values)
            {
                if (!prop.CanRead)
                    continue;

                var value = prop.GetValue(entity);
                columns += $"{prop.ColumnName}, ";
                values += $"@{prop.PropertyName}, ";
                command.AddParameter(prop.PropertyName, value ?? DBNull.Value);
            }
            if (command.Parameters.Count == 0)
                throw new DataException("No values were added to the query for " + entity);

            command.CommandText = string.Format("INSERT INTO {0} ({1}) VALUES({2})",
                TableName,
                columns.Remove(columns.Length - 2, 2),
                values.Remove(values.Length - 2, 2));
        }

        /// <summary>
        ///     Create an update query from the entity.
        /// </summary>
        /// <param name="command">Command to modify</param>
        /// <param name="entity">Entity to update</param>
        /// <exception cref="System.ArgumentNullException">
        ///     command
        ///     or
        ///     entity
        /// </exception>
        /// <exception cref="MappingException">Could not construct a correct SQL statement.</exception>
        public void UpdateCommand(IDbCommand command, object entity)
        {
            if (command == null) throw new ArgumentNullException("command");
            if (entity == null) throw new ArgumentNullException("entity");
            if (_keys.Count == 0)
                throw new MappingException(entity.GetType(), "The mapping has no primary keys defined.");

            var updates = "";
            var where = "";
            foreach (var property in _values)
            {
                if (!property.CanRead)
                    continue;

                var value = property.GetValue(entity);
                updates += $"{property.ColumnName}=@{property.PropertyName}, ";
                command.AddParameter(property.PropertyName, value);
            }
            if (command.Parameters.Count == 0)
                throw new DataException("At least one property (other than primary keys) must be specified.");

            foreach (var property in _keys)
            {
                var value = property.GetValue(entity);
                if (value == null || value == DBNull.Value)
                    throw new MappingException(entity.GetType(),
                        $"Entity {entity}' do not contain a value for the key property '{property.PropertyName}'");
                where += property.ColumnName + "=" + "@" + property.PropertyName + " AND ";
                command.AddParameter(property.PropertyName, value);
            }

            if (updates.Length < 2 || where.Length < 5)
                throw new MappingException(entity.GetType(),
                    $"Could not construct a proper UPDATE command. Is your mapping for '{entity.GetType().FullName}' correct?\r\n UPDATE clause '{updates}'\r\n WHERE clause '{@where}'");

            command.CommandText = string.Format("UPDATE {0} SET {1} WHERE {2}",
                TableName,
                updates.Remove(updates.Length - 2, 2),
                @where.Remove(@where.Length - 5, 5));
        }

        /// <summary>
        ///     Modifies the command to execute a DELETE statement
        /// </summary>
        /// <param name="command">Command that will be executed after this method call</param>
        /// <param name="entity">Only primary key properties are used in the WHERE clause</param>
        /// <exception cref="System.ArgumentNullException">
        ///     command
        ///     or
        ///     entity
        /// </exception>
        /// <exception cref="MappingException">Could not construct a correct SQL statement.</exception>
        public void DeleteCommand(IDbCommand command, object entity)
        {
            if (command == null) throw new ArgumentNullException("command");
            if (entity == null) throw new ArgumentNullException("entity");
            if (_keys.Count == 0)
                throw new MappingException(entity.GetType(),  "The mapping has no primary keys defined.");

            var where = "";
            foreach (var property in _keys)
            {
                var value = property.GetValue(entity);
                if (value == null || value == DBNull.Value)
                    throw new MappingException(entity.GetType(),
                        $"Entity {entity}' do not contain a value for the key property '{property.PropertyName}'");

                where += string.Format("{0}=" + "@{1} AND ", property.ColumnName, property.PropertyName);
                command.AddParameter(property.PropertyName, value);
            }

            command.CommandText = string.Format("DELETE FROM {0} WHERE {1}",
                TableName,
                @where.Remove(@where.Length - 5, 5));
        }

        /// <summary>
        ///     Truncate all rows in a table
        /// </summary>
        /// <param name="command">Command that will be executed after this method call</param>
        /// <exception cref="System.ArgumentNullException">command</exception>
        /// <remarks>
        ///     Will do a DELETE statement
        /// </remarks>
        public virtual void TruncateCommand(IDbCommand command)
        {
            if (command == null) throw new ArgumentNullException("command");

            command.CommandText = string.Format("DELETE FROM {0}", TableName);
        }

        /// <summary>
        ///     Modify SQL statement so that the result is paged.
        /// </summary>
        /// <param name="command">Command to modify</param>
        /// <param name="pageNumber">One based index</param>
        /// <param name="pageSize">Items per page.</param>
        public virtual void Paging(IDbCommand command, int pageNumber, int pageSize)
        {
            throw new NotSupportedException();
        }
    }
}